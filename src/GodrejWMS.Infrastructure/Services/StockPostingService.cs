using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>See <see cref="IStockPostingService"/>. The only code that edits <see cref="StockBatch"/> quantities for stock flows.</summary>
public sealed class StockPostingService(
    IApplicationDbContext db,
    IVirtualLocationService virtualLocations,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IStockPostingService
{
    public async Task<string> ApplyAsync(StockPostingContext context, IReadOnlyList<StockChange> changes, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var postingNumber = $"PST-{now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}";
        var warehouseByRack = new Dictionary<int, int>();
        var decreased = new List<PalletPosition>();
        var increased = new List<PalletPosition>();

        // One cell may be touched several times in a posting (a move then a move back): net them first.
        var merged = changes
            .Where(c => c.DeltaBoxes != 0)
            .GroupBy(c => (c.MaterialId, c.MfgMonth, c.StockSubtypeId, c.Position))
            .Select(g => new StockChange(g.Key.MaterialId, g.Key.MfgMonth, g.Key.StockSubtypeId, g.Key.Position, g.Sum(c => c.DeltaBoxes)))
            .Where(c => c.DeltaBoxes != 0)
            // Removals first, so a posting that moves boxes out and back in can never see a transient negative.
            .OrderBy(c => c.DeltaBoxes > 0)
            .ToList();

        foreach (var change in merged)
        {
            var position = change.Position;
            var batch = await FindBatchAsync(change, cancellationToken);
            if (batch is null)
            {
                if (change.DeltaBoxes < 0)
                {
                    throw new InvalidOperationException(
                        $"There is no stock to take at {position.LocationCode} (material cell not found).");
                }

                batch = new StockBatch
                {
                    MaterialId = change.MaterialId,
                    MfgMonth = change.MfgMonth,
                    StockSubtypeId = change.StockSubtypeId,
                    PalletPosition = position,
                    QuantityBoxes = 0
                };
                db.StockBatches.Add(batch);
            }

            var newQuantity = batch.QuantityBoxes + change.DeltaBoxes;
            if (newQuantity < 0)
            {
                throw new InvalidOperationException(
                    $"{position.LocationCode} holds only {batch.QuantityBoxes:0.###} box(es) of this stock; {-change.DeltaBoxes:0.###} cannot be taken.");
            }

            batch.QuantityBoxes = newQuantity;
            batch.RowVersion++;

            if (!warehouseByRack.TryGetValue(position.RackId, out var warehouseId))
            {
                warehouseId = position.Rack?.WarehouseId
                    ?? await db.Racks.AsNoTracking().Where(r => r.Id == position.RackId).Select(r => r.WarehouseId).FirstAsync(cancellationToken);
                warehouseByRack[position.RackId] = warehouseId;
            }

            db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                PostingNumber = postingNumber,
                EntryType = context.EntryType,
                WarehouseId = warehouseId,
                MaterialId = change.MaterialId,
                MfgMonth = change.MfgMonth,
                StockSubtypeId = change.StockSubtypeId,
                PalletPosition = position,
                QuantityBoxes = change.DeltaBoxes,
                BalanceAfterBoxes = newQuantity,
                ReferenceType = context.ReferenceType,
                ReferenceNumber = context.ReferenceNumber,
                Notes = context.Notes,
                CreatedAt = now,
                CreatedByUserId = currentUser.UserId,
                CreatedByUserName = currentUser.UserName
            });

            (change.DeltaBoxes < 0 ? decreased : increased).Add(position);
        }

        // A virtual location that receives stock is in use (a moved-in pallet on a FREE one); one that was emptied is released.
        foreach (var position in increased.Distinct().Where(p => p.IsVirtual && p.VirtualStatus != VirtualLocationStatus.InUse))
        {
            position.VirtualStatus = VirtualLocationStatus.InUse;
            position.RowVersion++;
            db.VirtualLocationLogs.Add(new VirtualLocationLog { PalletPosition = position, AssignedAt = now });
        }

        foreach (var position in decreased.Distinct().Where(p => p.IsVirtual))
        {
            await virtualLocations.ReleaseIfEmptyAsync(position, cancellationToken);
        }

        return postingNumber;
    }

    private async Task<StockBatch?> FindBatchAsync(StockChange change, CancellationToken cancellationToken)
    {
        bool Matches(StockBatch b) =>
            b.MaterialId == change.MaterialId && b.MfgMonth == change.MfgMonth
            && (ReferenceEquals(b.PalletPosition, change.Position) || (change.Position.Id != 0 && b.PalletPositionId == change.Position.Id));

        // Pending in the tracker (added earlier in this unit of work) wins over what is stored.
        var tracked = db.ChangeTracker.Entries<StockBatch>()
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity)
            .FirstOrDefault(Matches);
        if (tracked is not null || change.Position.Id == 0)
        {
            return tracked;
        }

        return await db.StockBatches.FirstOrDefaultAsync(b =>
            b.MaterialId == change.MaterialId && b.MfgMonth == change.MfgMonth && b.PalletPositionId == change.Position.Id,
            cancellationToken);
    }
}
