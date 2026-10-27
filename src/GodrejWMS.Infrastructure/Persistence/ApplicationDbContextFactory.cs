using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Persistence;

/// <summary>Builds independent, short-lived AppDbContext instances outside the request/circuit's
/// shared scoped one - see <see cref="IApplicationDbContextFactory"/>. Uses a fixed,
/// non-authenticated current-user stand-in: every caller of this factory today (PermissionService)
/// only touches entities with no per-user query filter (RolePermissions), so the real caller
/// identity is never needed by the queries this context runs.</summary>
public sealed class ApplicationDbContextFactory(DbContextOptions<AppDbContext> options) : IApplicationDbContextFactory
{
    public Task<IApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IApplicationDbContext>(new AppDbContext(options, NoCurrentUser.Instance, SystemClock.Instance));

    private sealed class NoCurrentUser : ICurrentUserService
    {
        public static readonly NoCurrentUser Instance = new();
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class SystemClock : IDateTimeProvider
    {
        public static readonly SystemClock Instance = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
