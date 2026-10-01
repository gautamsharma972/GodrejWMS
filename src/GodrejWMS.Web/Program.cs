using GodrejWMS.Application;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure;
using GodrejWMS.Infrastructure.Identity;
using GodrejWMS.Infrastructure.Persistence.Seed;
using GodrejWMS.Web.Components;
using GodrejWMS.Web.Components.Account;
using GodrejWMS.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
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

builder.Services.AddDataProtection()
    .SetApplicationName("GodrejWMS")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys")));

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Operator));
    options.AddPolicy("RequireSupervisorOrAdmin", policy => policy.RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Operator));
    options.AddPolicy("RequireOperatorOrAdmin", policy => policy.RequireRole(RoleNames.Admin, RoleNames.Supervisor, RoleNames.Operator));
});

var app = builder.Build();

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

//await DbInitializer.MigrateAsync(app.Services);

app.Run();
