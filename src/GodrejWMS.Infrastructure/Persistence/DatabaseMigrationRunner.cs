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
            return;
        }

        logger.LogInformation(
            "Applying {MigrationCount} pending database schema migration(s): {Migrations}",
            pendingMigrations.Length,
            string.Join(", ", pendingMigrations));

        await db.Database.MigrateAsync();
    }
}
