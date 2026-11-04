using System.Text;
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
    });

// Same "Permission:<key>" policy convention as the Blazor app's PermissionPolicyProvider /
// PermissionAuthorizationHandler, deliberately duplicated into this project's own Authorization
// folder rather than referencing GodrejWMS.Web - see the plan for why.
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapWarehousesEndpoints();
api.MapMaterialsEndpoints();
api.MapInwardNoPkmEndpoints();
api.MapRacksEndpoints();
api.MapPulloutEndpoints();

// Same shared database as GodrejWMS.Web, which already applies pending migrations at startup -
// mirrored here (not copied from Web, just the same Infrastructure-layer helper both hosts share)
// so this API works correctly even if it's started before the Web app ever has been. Idempotent.
await GodrejWMS.Infrastructure.Persistence.DatabaseMigrationRunner.ApplyPendingAsync(app.Services);

app.Run();
