using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Builds the shared stock services on a test database the way the application wires them.</summary>
internal static class TestServices
{
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    public static VirtualLocationService Virtuals(AppDbContext db) => new(db, new SvcClock());

    public static StockPostingService Posting(AppDbContext db) =>
        new(db, Virtuals(db), new SvcClock(), new SvcUser());

    public static InventoryMovementService MovementService(AppDbContext db) =>
        new(db, new SvcClock(), new SvcUser(), Posting(db));
}

internal sealed class SvcClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow => TestServices.Now;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

internal sealed class SvcUser : ICurrentUserService
{
    public string? UserId => "svc-user";
    public string? UserName => "svc@godrejwms.local";
    public bool IsInRole(string role) => true;
    public IReadOnlyList<string> Roles => ["Superadmin"];
}
