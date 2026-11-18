using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GodrejWMS.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations only. Application/demo/reference-data seeders are
/// intentionally not part of the production startup path.
/// </summary>
public static class DatabaseMigrationRunner
{
    public static async Task ApplyPendingAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DatabaseMigrationRunner));

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pendingMigrations.Length == 0)
        {
            logger.LogInformation("Database schema is current; no pending migrations were found.");
        }
        else
        {
            logger.LogInformation(
                "Applying {MigrationCount} pending database schema migration(s): {Migrations}",
                pendingMigrations.Length,
                string.Join(", ", pendingMigrations));

            await db.Database.MigrateAsync();
        }

        await EnsureSystemZoneTypesAsync(db, logger);
    }

    /// <summary>
    /// The Overflow (virtual locations) and GIT (picker set-aside) zone types are system rows the application itself needs: a
    /// location cannot be created in a zone that does not exist. They used to be inserted only by migration SQL compiled in under
    /// INCLUDE_MIGRATION_SEED_DATA, which no build defines, so a database built from the migrations alone never got them and
    /// "set aside as GIT" failed on a foreign key. This is not sample data: it is idempotent and runs on every start, only
    /// adding a row that is missing.
    /// </summary>
    private static async Task EnsureSystemZoneTypesAsync(AppDbContext db, ILogger logger)
    {
        var required = new (int Id, string Code, string Name, string Description, int SortOrder)[]
        {
            (ZoneTypeIds.Overflow, ZoneTypeCodes.Overflow, "Overflow", "Auto-generated virtual overflow locations", 90),
            (ZoneTypeIds.Git, ZoneTypeCodes.Git, "GIT", "Picker set-aside stock, not yet racked", 91)
        };

        foreach (var zone in required)
        {
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ZoneTypes (Id, Code, DisplayName, Description, SortOrder, IsActive, CreatedAt)
                SELECT {zone.Id}, {zone.Code}, {zone.Name}, {zone.Description}, {zone.SortOrder}, 1, UTC_TIMESTAMP(6)
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM ZoneTypes WHERE Id = {zone.Id})
                  AND NOT EXISTS (SELECT 1 FROM ZoneTypes WHERE Code = {zone.Code})");
            if (inserted > 0)
            {
                logger.LogWarning("System zone type '{Code}' (id {Id}) was missing and has been created.", zone.Code, zone.Id);
            }
            else if (!await db.ZoneTypes.AsNoTracking().AnyAsync(z => z.Id == zone.Id))
            {
                logger.LogError(
                    "A zone type with code '{Code}' exists under a different id than the expected {Id}; features that need it will fail until it is corrected.",
                    zone.Code, zone.Id);
            }
        }
    }
}
