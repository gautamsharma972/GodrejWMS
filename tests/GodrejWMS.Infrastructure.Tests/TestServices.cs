using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Builds the shared stock services on a test database the way the application wires them.</summary>
internal static class TestServices
{
    public static readonly DateTimeOffset Now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    public static VirtualLocationService Virtuals(AppDbContext db) => new(db, new SvcClock());

    public static GitLocationService Git(AppDbContext db) => new(db);

    public static StockPostingService Posting(AppDbContext db) =>
        new(db, Virtuals(db), new SvcClock(), new SvcUser());

    public static GodrejWMS.Application.Features.Inward.InwardPlacementService Placement(AppDbContext db, IPalletAllocationService allocator) =>
        new(db, allocator, Posting(db), Virtuals(db), new SvcClock(), new SvcUser());

    public static EfUnitOfWork Uow(AppDbContext db) => new(db);

    /// <summary>A GRN with open (unposted) reservations - what the old reserve-then-confirm upload produced and what the
    /// no-PKM flow still produces - for tests of Refresh / Reallocate / Confirm.</summary>
    public static async Task<GodrejWMS.Domain.Entities.InwardTransaction> PendingGrnAsync(
        AppDbContext db, IPalletAllocationService allocator, params (GodrejWMS.Domain.Entities.Material Material, int Quantity, int MfgMonth)[] lines)
    {
        var grn = new GodrejWMS.Domain.Entities.InwardTransaction { WarehouseId = 1, ReferenceNumber = "GRN-PENDING-" + Guid.NewGuid().ToString("N")[..8] };
        db.InwardTransactions.Add(grn);
        await db.SaveChangesAsync();
        foreach (var (material, quantity, mfgMonth) in lines)
        {
            var allocation = await allocator.AllocateAsync(material.Id, quantity, mfgMonth, 1);
            var line = new GodrejWMS.Domain.Entities.InwardTransactionLine
            {
                MaterialId = material.Id, MfgMonth = mfgMonth, RequestedQuantityBoxes = quantity,
                AllocatedQuantityBoxes = allocation.AllocatedQuantityBoxes, Status = allocation.Status, Remarks = allocation.Remarks
            };
            foreach (var suggestion in allocation.Lines)
            {
                var position = db.PalletPositions.First(p => p.LocationCode == suggestion.LocationCode);
                position.RowVersion++;
                line.Putaways.Add(new GodrejWMS.Domain.Entities.InwardPutaway
                {
                    PalletPositionId = position.Id, QuantityBoxes = suggestion.QuantityBoxes, AllocationReason = suggestion.Reason, IsConfirmed = false
                });
            }

            grn.Lines.Add(line);
            await db.SaveChangesAsync();
        }

        return grn;
    }

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
