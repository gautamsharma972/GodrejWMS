using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>
/// FIFO pick engine: drains the oldest manufacturing month first, and within a PKM works through
/// locations in Rack -> Column -> Level order - a column is emptied from Level 1 upward
/// (A-01-01, A-01-02, ...) before moving to the next column - mirroring the put-away fill order.
/// Only good stock in active good locations is pickable; damage, expired, and hold stock remain isolated.
/// </summary>
public class PulloutAllocationService(IApplicationDbContext db, IOptions<PickingOptions>? options = null) : IPulloutAllocationService
{
    public async Task<PulloutAllocationResult> AllocateAsync(
        int materialId,
        decimal quantityBoxes,
        int warehouseId,
        CancellationToken cancellationToken = default,
        bool preview = false,
        int? excludePulloutTransactionId = null)
    {
        var remaining = quantityBoxes;
        var picks = new List<PulloutPickLine>();
        var virtualFirst = options?.Value.PickFromVirtualFirst ?? new PickingOptions().PickFromVirtualFirst;

        var query = db.StockBatches.AsQueryable();
        if (preview) query = query.AsNoTracking();

        var batches = await query
            .Where(b => b.MaterialId == materialId
                && b.StockSubtypeId == LocationSubtypeIds.Good
                && b.QuantityBoxes > 0
                && b.PalletPosition.IsActive
                && b.PalletPosition.LocationSubtypeId == LocationSubtypeIds.Good
                && b.PalletPosition.Rack.WarehouseId == warehouseId)
            .Include(b => b.PalletPosition)
            // 1. FIFO: oldest PKM first - never broken by anything below.
            .OrderBy(b => b.MfgMonth)
            // 2. Partial pallets before full ones (a cell holding less than a full pallet of the material), so they get cleared.
            .ThenBy(b => b.QuantityBoxes < b.Material.PalletCapacityBoxes * b.PalletPosition.MaxPallets ? 0 : 1)
            // 3. Same PKM and fullness: virtual overflow locations first (when configured) so they empty out and get reused.
            .ThenBy(b => virtualFirst && b.PalletPosition.IsVirtual ? 0 : 1)
            // 4. Ground level (level 1) first, then upward; then rack and column.
            .ThenBy(b => b.PalletPosition.Level)
            .ThenBy(b => b.PalletPosition.Rack.Code)
            .ThenBy(b => b.PalletPosition.Column)
            .ThenBy(b => b.PalletPosition.LocationCode)
            .ToListAsync(cancellationToken);

        var reserved = await db.PulloutPicks.AsNoTracking()
            .Where(p => p.PulloutTransactionLine.MaterialId == materialId
                && p.PulloutTransactionLine.PulloutTransaction.WarehouseId == warehouseId
                && !p.PulloutTransactionLine.PulloutTransaction.IsConfirmed
                && !p.PulloutTransactionLine.PulloutTransaction.IsRejected
                && (!excludePulloutTransactionId.HasValue
                    || p.PulloutTransactionLine.PulloutTransactionId != excludePulloutTransactionId.Value))
            .GroupBy(p => new { p.PalletPositionId, p.MfgMonth })
            .Select(group => new
            {
                group.Key.PalletPositionId,
                group.Key.MfgMonth,
                QuantityBoxes = group.Sum(p => p.QuantityBoxes)
            })
            .ToDictionaryAsync(x => (x.PalletPositionId, x.MfgMonth), x => x.QuantityBoxes, cancellationToken);

        foreach (var batch in batches)
        {
            if (remaining <= 0)
            {
                break;
            }

            var available = Math.Max(0, batch.QuantityBoxes
                - reserved.GetValueOrDefault((batch.PalletPositionId, batch.MfgMonth)));
            var take = Math.Min(remaining, available);
            if (take <= 0) continue;
            batch.QuantityBoxes -= take;
            batch.RowVersion++;
            remaining -= take;

            picks.Add(new PulloutPickLine(batch.PalletPosition.LocationCode, batch.MfgMonth, take));

            if (!preview && batch.QuantityBoxes <= 0)
            {
                db.StockBatches.Remove(batch);
            }
        }

        var picked = quantityBoxes - remaining;
        var status = remaining <= 0
            ? AllocationStatus.Fulfilled
            : picked > 0
                ? AllocationStatus.Partial
                : AllocationStatus.Failed;

        var remarks = status == AllocationStatus.Fulfilled
            ? null
            : $"Insufficient good stock - {remaining:0.###} box(es) short across all pickable locations.";

        return new PulloutAllocationResult(status, picked, picks, remarks);
    }
}
