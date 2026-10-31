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
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { },
    KnownProxies = { }
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
        app.Logger.LogWarning(ex,
            "Rejected invalid antiforgery form submission (thrown) for {Method} {Path}; redirecting to sign in.",
            context.Request.Method,
            context.Request.Path);
        context.Response.Redirect("/Account/Login");
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
        app.Logger.LogWarning(
            "Rejected stale or invalid antiforgery form submission for {Method} {Path}; redirecting to sign in.",
            context.Request.Method,
            context.Request.Path);
        context.Response.Redirect("/Account/Login");
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

// Local development only: opt-in via DevSeed:Enabled (never set in appsettings.json). Needs
// DevSeed:Password for the seeded logins; see DevelopmentDataSeeder.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DevSeed:Enabled"))
{
    var devPassword = app.Configuration["DevSeed:Password"]
        ?? throw new InvalidOperationException("DevSeed:Enabled requires DevSeed:Password to be set.");
    await DevelopmentDataSeeder.SeedAsync(app.Services, devPassword);
}

app.Run();

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
