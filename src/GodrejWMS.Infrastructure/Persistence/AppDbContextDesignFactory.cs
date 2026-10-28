using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GodrejWMS.Infrastructure.Persistence;

/// <summary>
/// Keeps EF migration tooling independent from the web application's startup migration runner.
/// The connection is never opened while scaffolding a migration.
/// </summary>
public sealed class AppDbContextDesignFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                "Server=localhost;Database=godrejwms_design;User=design;Password=design;",
                new MySqlServerVersion(new Version(8, 4, 7)))
            .Options;

        return new AppDbContext(options, new DesignCurrentUser(), new DesignClock());
    }

    private sealed class DesignCurrentUser : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => true;
        public IReadOnlyList<string> Roles => ["Superadmin"];
    }

    private sealed class DesignClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
