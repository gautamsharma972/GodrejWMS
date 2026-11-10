using System.Text;
using System.Threading.RateLimiting;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using GodrejWMS.Application;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure;
using GodrejWMS.PickersApi.Auth;
using GodrejWMS.PickersApi.Authorization;
using GodrejWMS.PickersApi.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Same Application/Infrastructure layers the Blazor app uses - identical MediatR handlers,
// AppDbContext, Identity stores, IExcelService, IWarehouseAccessService, etc. GodrejWMS.Web is
// never referenced; this is a fully separate deployable (see the Native Android Pickers app plan).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
builder.Services.AddSingleton<IRealtimeNotificationService, GodrejWMS.PickersApi.NoOpRealtimeNotificationService>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<JwtTokenService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

// A weak or shared signing key lets anyone mint tokens: refuse to start with one. The development key is only acceptable in Development.
if (Encoding.UTF8.GetByteCount(jwtOptions.Key) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters. Set it through an environment variable or secret store.");
}

if (!builder.Environment.IsDevelopment()
    && (jwtOptions.Key.StartsWith("dev-", StringComparison.OrdinalIgnoreCase)
        || jwtOptions.Key.Contains("change", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("Jwt:Key looks like a development/placeholder key. Set a unique production key (Jwt__Key).");
}

// Login throttling per client address, on top of the per-account lockout.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// AddInfrastructure() -> AddIdentity() already registered a cookie scheme ("Identity.Application")
// and explicitly set it as the DefaultAuthenticateScheme/DefaultChallengeScheme - the single-string
// AddAuthentication(scheme) overload below only sets DefaultScheme, which does NOT override those
// two, so an unauthenticated request would otherwise 302-redirect to the cookie scheme's LoginPath
// instead of returning a plain 401. Setting all three explicitly here (registered after AddIdentity,
// so it wins) makes JWT bearer the only scheme this API ever uses.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
        // A token stops working the moment its user is disabled, deleted or has their password/roles changed (security stamp),
        // instead of living on for the rest of its lifetime.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var user = userId is null ? null : await userManager.FindByIdAsync(userId);
                var stamp = context.Principal?.FindFirst(JwtTokenService.SecurityStampClaim)?.Value;
                if (user is null || !user.IsActive || stamp is null || stamp != user.SecurityStamp)
                {
                    context.Fail("The session is no longer valid. Please sign in again.");
                }
            }
        };
    });

// Same "Permission:<key>" policy convention as the Blazor app's PermissionPolicyProvider /
// PermissionAuthorizationHandler, deliberately duplicated into this project's own Authorization
// folder rather than referencing GodrejWMS.Web - see the plan for why.
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddOpenApi();

var app = builder.Build();

// Trust forwarded headers only from the reverse proxy (loopback + private networks, or ForwardedHeaders:KnownProxies/KnownNetworks).
var forwardedOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
var configuredProxies = app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
var configuredNetworks = app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
foreach (var proxy in configuredProxies)
{
    forwardedOptions.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
}

foreach (var network in configuredNetworks)
{
    var parts = network.Split('/');
    forwardedOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1])));
}

if (configuredProxies.Length == 0 && configuredNetworks.Length == 0)
{
    forwardedOptions.KnownProxies.Add(System.Net.IPAddress.Loopback);
    forwardedOptions.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
    foreach (var (prefix, length) in new[] { ("10.0.0.0", 8), ("172.16.0.0", 12), ("192.168.0.0", 16), ("fc00::", 7) })
    {
        forwardedOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse(prefix), length));
    }
}

app.UseForwardedHeaders(forwardedOptions);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // TLS ends at the reverse proxy, so no HTTP->HTTPS redirect here; HSTS tells clients to keep using HTTPS.
    app.UseHsts();
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "Something went wrong. Please try again." });
    }));
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await next();
});
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapWarehousesEndpoints();
api.MapMaterialsEndpoints();
api.MapInwardNoPkmEndpoints();
api.MapRacksEndpoints();
api.MapPulloutEndpoints();
api.MapPickerPutawayEndpoints();

// Same shared database as GodrejWMS.Web, which already applies pending migrations at startup -
// mirrored here (not copied from Web, just the same Infrastructure-layer helper both hosts share)
// so this API works correctly even if it's started before the Web app ever has been. Idempotent.
await GodrejWMS.Infrastructure.Persistence.DatabaseMigrationRunner.ApplyPendingAsync(app.Services);

app.Run();
