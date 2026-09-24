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

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public string? UserId => "test-user";

        public string? UserName => "test@godrejwms.local";

        public bool IsInRole(string role) => true;
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        public DateOnly Today => new(2026, 8, 25);
    }
}
