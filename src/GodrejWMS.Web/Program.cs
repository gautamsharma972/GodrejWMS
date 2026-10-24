using GodrejWMS.Application;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure;
using GodrejWMS.Infrastructure.Identity;
using GodrejWMS.Infrastructure.Persistence.Seed;
using GodrejWMS.Web.Components;
using GodrejWMS.Web.Components.Account;
using GodrejWMS.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Operator));
    options.AddPolicy("RequireSupervisorOrAdmin", policy => policy.RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Operator));
    options.AddPolicy("RequireOperatorOrAdmin", policy => policy.RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Operator));
});

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// Initial database migrations and seed data are intentionally disabled at startup.
// await DbInitializer.MigrateAsync(app.Services);

app.Run();
