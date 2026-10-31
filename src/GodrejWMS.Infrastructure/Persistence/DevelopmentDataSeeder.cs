using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GodrejWMS.Infrastructure.Persistence;

/// <summary>
/// Local-development-only data that migrations don't provide: the built-in Identity roles that a
/// fresh database is missing, one login per role, movement reasons and the season-month map.
/// Never part of the normal startup path - Program.cs only calls this in the Development
/// environment and only when DevSeed:Enabled is true. Idempotent: existing rows are left alone.
/// The login password comes from configuration (DevSeed:Password) rather than a literal here.
/// </summary>
public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, string password)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevelopmentDataSeeder));

        await SeedRolesAsync(provider);
        await SeedMovementReasonsAsync(db);
        await SeedSeasonMonthMapAsync(db);
        await SeedUsersAsync(provider, db, password, logger);
    }

    private static async Task SeedRolesAsync(IServiceProvider provider)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedMovementReasonsAsync(AppDbContext db)
    {
        if (await db.MovementReasons.AnyAsync())
        {
            return;
        }

        db.MovementReasons.AddRange(
            new MovementReason { Code = MovementReasonCodes.RackReorganization, DisplayName = "Rack Reorganization", SortOrder = 10, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.SpaceOptimization, DisplayName = "Space Optimization", SortOrder = 20, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.StockConsolidation, DisplayName = "Stock Consolidation", SortOrder = 30, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.OperationalRequirement, DisplayName = "Operational Requirement", SortOrder = 40, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.SupervisorCorrection, DisplayName = "Supervisor Correction", SortOrder = 50, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSeasonMonthMapAsync(AppDbContext db)
    {
        var existing = (await db.SeasonMonthMaps.Select(s => s.Month).ToListAsync()).ToHashSet();
        int[] seasonByMonth =
        [
            SeasonIds.Winter, SeasonIds.Winter, SeasonIds.Summer, SeasonIds.Summer, SeasonIds.Summer, SeasonIds.Summer,
            SeasonIds.Rainy, SeasonIds.Rainy, SeasonIds.Rainy, SeasonIds.Rainy, SeasonIds.Winter, SeasonIds.Winter
        ];

        for (var month = 1; month <= 12; month++)
        {
            if (!existing.Contains(month))
            {
                db.SeasonMonthMaps.Add(new SeasonMonthMap { Month = month, SeasonId = seasonByMonth[month - 1] });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedUsersAsync(IServiceProvider provider, AppDbContext db, string password, ILogger logger)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var warehouseId = await db.Warehouses.OrderBy(w => w.Id).Select(w => (int?)w.Id).FirstOrDefaultAsync();
        var regionId = await db.Regions.OrderBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync();

        var accounts = new[]
        {
            (Email: "superadmin@godrejwms.local", FullName: "Superadmin", Role: RoleNames.Superadmin),
            (Email: "logisticmanager@godrejwms.local", FullName: "Logistic Manager", Role: RoleNames.LogisticManager),
            (Email: "headsupervisor@godrejwms.local", FullName: "Head Supervisor", Role: RoleNames.HeadSupervisor),
            (Email: "officestaff@godrejwms.local", FullName: "Office Staff", Role: RoleNames.OfficeStaff),
            (Email: "supervisor@godrejwms.local", FullName: "Supervisor", Role: RoleNames.Supervisor)
        };

        foreach (var account in accounts)
        {
            if (await userManager.FindByEmailAsync(account.Email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                FullName = account.FullName,
                IsActive = true
            };
            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                logger.LogWarning("Could not seed {Email}: {Errors}", account.Email, string.Join("; ", created.Errors.Select(e => e.Description)));
                continue;
            }

            await userManager.AddToRoleAsync(user, account.Role);

            // Scope: Superadmin is org-wide; Logistic Manager by region; everyone else by one warehouse.
            if (account.Role == RoleNames.LogisticManager && regionId is not null)
            {
                db.UserRegions.Add(new UserRegion { UserId = user.Id, RegionId = regionId.Value });
            }
            else if (account.Role != RoleNames.Superadmin && warehouseId is not null)
            {
                db.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = warehouseId.Value });
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded development account {Email} ({Role}).", account.Email, account.Role);
        }
    }
}
