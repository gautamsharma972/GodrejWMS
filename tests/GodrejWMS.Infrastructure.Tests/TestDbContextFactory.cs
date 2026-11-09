using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Tests;

internal static class TestDbContextFactory
{
    public static AppDbContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>Creates a context against a named in-memory database, so a second call with the
    /// same name gets an independent <see cref="AppDbContext"/> instance sharing the same
    /// underlying store - used to simulate two concurrent requests racing for real.</summary>
    public static AppDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options, new TestCurrentUser(), new TestClock());
    }

    /// <summary>A context acting as the given user, so the warehouse/region query filters apply as they do in production
    /// (the default context is a Superadmin and sees everything).</summary>
    public static AppDbContext CreateAs(string databaseName, string userId, params string[] roles)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new AppDbContext(options, new ScopedUser(userId, roles), new TestClock());
    }

    private sealed class ScopedUser(string userId, string[] roles) : ICurrentUserService
    {
        public string? UserId => userId;
        public string? UserName => userId;
        public IReadOnlyList<string> Roles => roles;
        public bool IsInRole(string role) => roles.Contains(role);
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-user";

        public string? UserName => "test@godrejwms.local";

        public bool IsInRole(string role) => true;

        public IReadOnlyList<string> Roles => ["Superadmin"];
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        public DateOnly Today => new(2026, 8, 25);
    }
}
