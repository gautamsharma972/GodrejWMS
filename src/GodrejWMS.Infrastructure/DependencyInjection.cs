using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Identity;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GodrejWMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' was not found in configuration.");

        // A fixed server version (rather than ServerVersion.AutoDetect) avoids opening a
        // connection just to configure the DbContext — important for `dotnet ef migrations add`,
        // which must work with no MySQL server running yet. Adjust if your MySQL server differs.
        var serverVersion = new MySqlServerVersion(new Version(8, 4, 7));

        void ConfigureDbContext(DbContextOptionsBuilder options) =>
            options.UseMySql(connectionString, serverVersion, mySqlOptions =>
                    mySqlOptions.EnableRetryOnFailure(maxRetryCount: 3))
                .AddInterceptors(new ForceInnoDbInterceptor());

        services.AddDbContext<AppDbContext>(ConfigureDbContext);
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // A separate factory (not the scoped AppDbContext above) for read-only checks that run
        // from Blazor components (e.g. PermissionService) - those can fire concurrently with
        // whatever DbContext operation the page's own handler is awaiting on the shared scoped
        // instance, and EF Core forbids two concurrent operations on one DbContext. Giving these
        // their own short-lived context sidesteps that race entirely. Built directly from a fresh
        // DbContextOptionsBuilder per call (not DI-resolved DbContextOptions<AppDbContext>, which
        // is registered Scoped by AddDbContext and can't be consumed by this Singleton factory).
        services.AddSingleton<IApplicationDbContextFactory>(_ =>
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            ConfigureDbContext(builder);
            return new ApplicationDbContextFactory(builder.Options);
        });

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IExcelService, ClosedXmlExcelService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleManagementService, RoleManagementService>();
        services.AddScoped<IUserDisplayNameService, UserDisplayNameService>();
        services.AddScoped<IPalletAllocationService, PalletAllocationService>();
        services.AddScoped<IPulloutAllocationService, PulloutAllocationService>();
        services.AddScoped<IInventoryMovementService, InventoryMovementService>();

        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.AddHttpClient<IAiAssistantService, OllamaAiAssistantService>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<IOptions<AiOptions>>().Value.OllamaBaseUrl;
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        return services;
    }
}
