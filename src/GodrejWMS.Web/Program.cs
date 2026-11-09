using GodrejWMS.Application;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure;
using GodrejWMS.Infrastructure.Identity;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Web.Components;
using GodrejWMS.Web.Components.Account;
using GodrejWMS.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Application / Infrastructure (Clean Architecture layers) ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Blazor Web App (Interactive Server) ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<IRealtimeNotificationService, RealtimeNotificationService>();

builder.Services.AddDataProtection()
    .SetApplicationName("GodrejWMS")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys")));

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.Configure<IdleSessionOptions>(builder.Configuration.GetSection("Session"));
builder.Services.Configure<GodrejWMS.Application.Features.StockMaster.Queries.StockCapacityOptions>(builder.Configuration.GetSection("Capacity"));
var idleSessionOptions = builder.Configuration.GetSection("Session").Get<IdleSessionOptions>() ?? new IdleSessionOptions();
var idleTimeout = TimeSpan.FromMinutes(Math.Max(1, idleSessionOptions.IdleTimeoutMinutes));

// Backstop for the client-side idle watcher (see idle-timeout.js / IdleTimeoutWatcher.razor):
// the auth cookie itself expires after the same window of no HTTP traffic reaching the server,
// so a user is logged out even if JS never runs (blocked, tab closed and reopened later, etc).
// SlidingExpiration renews it on every authenticated request, so active users are never cut off.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = idleTimeout;
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Behind the TLS-terminating proxy the forwarded-proto header makes requests HTTPS, so Always is safe there;
    // plain-HTTP local development keeps working.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization();

// Lets page-level [Authorize(Policy = "Permission:<key>")] attributes be driven by the same
// Role & Permission Management matrix the nav menu already uses, instead of a hardcoded role list
// - see GodrejWMS.Web/Authorization/PermissionPolicyProvider.cs.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, GodrejWMS.Web.Authorization.PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, GodrejWMS.Web.Authorization.PermissionAuthorizationHandler>();

var app = builder.Build();

// Production runs behind a reverse proxy (gcpl.logivue.in) that terminates HTTPS and forwards
// plain HTTP to Kestrel. Without this, the app thinks every request is HTTP, so Blazor's
// NavigationManager computes an "http://..." base URI while the browser is actually on
// "https://...", which throws when redirecting after login. KnownNetworks/KnownProxies are
// cleared because the proxy's address isn't a fixed, known value (e.g. a load balancer) - the
// standard pattern for this deployment shape, per the ASP.NET Core docs on forwarded headers.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
// Trust forwarded headers only from the reverse proxy: loopback (same host) plus private networks by default, or exactly what
// ForwardedHeaders:KnownProxies / KnownNetworks name. A client on a public address can no longer fake its IP or "HTTPS".
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

// Browser hardening headers on every response.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "geolocation=(self), camera=(self), microphone=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com data:; img-src 'self' data: blob:; connect-src 'self' ws: wss:; " +
            "worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });
    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// A long-lived page can submit a form token that belongs to an expired or replaced session (or one
// from before a server restart, which rotates the Data Protection keys the token was encrypted
// with). Most of those AntiforgeryMiddleware (registered below) handles by recording a failed
// verdict and continuing - caught by the feature check just after it. But some invalid/stale-token
// shapes make its internals throw outright instead (seen in production as a raw
// InvalidOperationException from DefaultAntiforgeryTokenStore, before any verdict is ever
// recorded) - letting that reach UseExceptionHandler/UseStatusCodePagesWithReExecute re-executes
// the same broken request a second time and crashes again, which is what was surfacing as an
// unhandled exception. This wraps UseAntiforgery() itself so any exception it throws also ends in
// the same recoverable sign-in redirect instead.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!context.Response.HasStarted && IsAntiforgeryFailure(ex))
    {
        app.Logger.LogDebug(ex, "Antiforgery validation threw for {Method} {Path}.", context.Request.Method, context.Request.Path);
        await RedirectAfterRejectedAntiforgeryAsync(context, app.Logger);
    }
});

app.UseAntiforgery();

// The common case: AntiforgeryMiddleware recorded a failed verdict without throwing. Inspect it
// immediately after validation and short-circuit before endpoint/form processing, keeping
// antiforgery enforcement enabled while giving the user a recoverable fresh sign-in request
// instead of an unhandled production error.
app.Use(async (context, next) =>
{
    if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false })
    {
        await RedirectAfterRejectedAntiforgeryAsync(context, app.Logger);
        return;
    }

    await next();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// Apply pending database schema/table migrations. No application, demo-data, reference-data,
// role, or user seeders run during startup.
await DatabaseMigrationRunner.ApplyPendingAsync(app.Services);

#if DEBUG
// Local development only: opt-in via DevSeed:Enabled (never set in appsettings.json). Needs
// DevSeed:Password for the seeded logins; see DevelopmentDataSeeder. Compiled out of Release builds,
// so a published (production) build has no sample-data seeding at all.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DevSeed:Enabled"))
{
    var devPassword = app.Configuration["DevSeed:Password"]
        ?? throw new InvalidOperationException("DevSeed:Enabled requires DevSeed:Password to be set.");
    await DevelopmentDataSeeder.SeedAsync(app.Services, devPassword);
}
#endif

app.Run();

// Shared recovery for a rejected (stale/invalid) antiforgery form post, whether the middleware recorded a failed
// verdict or threw.
//  - Logout: the user asked to leave, and the usual reasons for a stale token - the idle timer's hidden form posting
//    after the cookie expired, or an old tab from a previous sign-in (another user on this browser) - are exactly when
//    they want out. Sign out anyway and go to the sign-in page (with ?reason=idle when the idle timer posted it). A forged logout is only a nuisance, never a data risk.
//  - Anything else: a second submit of a form whose first submit already worked (double click, Enter + click) arrives
//    signed in but carrying a token issued for the anonymous page. Send a signed-in user back to the app rather than
//    to the sign-in page; everyone else gets a fresh sign-in request.
static async Task RedirectAfterRejectedAntiforgeryAsync(HttpContext context, Microsoft.Extensions.Logging.ILogger logger)
{
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/Account/Logout", StringComparison.OrdinalIgnoreCase))
    {
        await context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
        logger.LogInformation("Logout submitted with a stale antiforgery token; signed the user out anyway.");
        // The form body can't be read once validation failed, so the idle timer's form flags itself in the query string.
        context.Response.Redirect(context.Request.Query.ContainsKey("idle") ? "/Account/Login?reason=idle" : "/Account/Login");
        return;
    }

    var signedIn = context.User.Identity?.IsAuthenticated == true;
    logger.LogWarning(
        "Rejected stale or invalid antiforgery form submission for {Method} {Path}; redirecting to {Target}.",
        context.Request.Method,
        context.Request.Path,
        signedIn ? "home" : "sign in");
    context.Response.Redirect(signedIn ? "/" : "/Account/Login");
}

// Covers every shape an invalid/stale antiforgery token has been observed to throw as: the
// documented AntiforgeryValidationException, a BadHttpRequestException whose message names
// "anti-forgery", and a raw InvalidOperationException out of DefaultAntiforgeryTokenStore whose
// message names "IAntiforgeryValidationFeature" - matched by namespace/message rather than an
// exact type list so a future framework-internal variant of the same failure is still caught here
// instead of reaching the user as a 500.
static bool IsAntiforgeryFailure(Exception ex) =>
    ex is AntiforgeryValidationException ||
    ex.GetType().Namespace?.StartsWith("Microsoft.AspNetCore.Antiforgery", StringComparison.Ordinal) == true ||
    ex.Message.Contains("anti-forgery", StringComparison.OrdinalIgnoreCase) ||
    ex.Message.Contains("antiforgery", StringComparison.OrdinalIgnoreCase);
