using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>
/// FIFO pick engine: drains the oldest manufacturing month first; within a PKM, virtual (overflow)
/// locations go before physical ones when configured. Within that, locations are preferred by
/// whether they are themselves a full pallet or a partial one - full-pallet locations are
/// preferred while the remaining quantity still needs at least one more full pallet, and
/// partial-pallet locations are preferred once it doesn't, re-evaluated after every pick so the
/// preference can flip mid-allocation. If the preferred kind runs out within the current
/// PKM/virtual-tier group, the other kind is used instead rather than skipping ahead. Finally,
/// locations are drained ground level first (across all columns), then upward, then by rack and
/// column - mirroring the put-away fill order. Before splitting a quantity over several locations, a single location of the
/// current PKM that can cover the whole remaining quantity is used (FIFO still decides the PKM first).
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
                && b.PalletPosition.Rack.WarehouseId == warehouseId
                // GIT stock is real but only ever reachable through a deliberate manual pick
                // (Change Pick Location), never the automatic FIFO allocator.
                && b.PalletPosition.ZoneTypeId != ZoneTypeIds.Git)
            .Include(b => b.PalletPosition)
            .Include(b => b.Material)
            // 1. FIFO: oldest PKM first - never broken by anything below.
            .OrderBy(b => b.MfgMonth)
            // 2. Same PKM: virtual overflow locations first (when configured) so they empty out and get reused.
            .ThenBy(b => virtualFirst && b.PalletPosition.IsVirtual ? 0 : 1)
            // 3. Full-vs-partial preference is dynamic (depends on the live remaining quantity) and is
            // applied in memory below, within each (PKM, virtual-tier) group.
            // 4. Ground level (level 1) first across all columns, then upward; then rack and column.
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

        // All batches are for one material, so its pallet capacity is one constant for this whole call.
        // Requirement-side check: "is what's still needed itself a full pallet's worth" - a
        // material/order trait, independent of any one location's MaxPallets.
        var palletCapacityBoxes = batches.Count > 0 ? (decimal)batches[0].Material.PalletCapacityBoxes : 0m;

        // Recover the (PKM, virtual-tier) buckets the static sort already produced - a grouping,
        // not a re-sort, since the list is already sorted contiguously by this key.
        var buckets = batches.GroupBy(b => (b.MfgMonth, Tier: virtualFirst && b.PalletPosition.IsVirtual ? 0 : 1));

        foreach (var bucket in buckets)
        {
            if (remaining <= 0) break;

            // Location-side check (unchanged formula): is this specific batch, at its own
            // position with its own MaxPallets, itself fully loaded. Sub-lists keep the bucket's
            // existing Level -> Rack -> Column -> LocationCode order.
            var full = new List<StockBatch>();
            var partial = new List<StockBatch>();
            foreach (var batch in bucket)
            {
                if (batch.QuantityBoxes >= batch.Material.PalletCapacityBoxes * batch.PalletPosition.MaxPallets)
                    full.Add(batch);
                else
                    partial.Add(batch);
            }

            // Whole quantity from ONE location when a single location of this PKM can cover it: no splitting across locations while
            // one has enough. Among the locations that can, the same full-vs-partial preference applies, then the lowest location first.
            decimal Available(StockBatch b) => Math.Max(0, b.QuantityBoxes - reserved.GetValueOrDefault((b.PalletPositionId, b.MfgMonth)));
            var single = (remaining >= palletCapacityBoxes ? full.Concat(partial) : partial.Concat(full))
                .FirstOrDefault(b => Available(b) >= remaining);
            if (single is not null)
            {
                single.QuantityBoxes -= remaining;
                single.RowVersion++;
                picks.Add(new PulloutPickLine(single.PalletPosition.LocationCode, single.MfgMonth, remaining));
                remaining = 0;
                if (!preview && single.QuantityBoxes <= 0)
                {
                    db.StockBatches.Remove(single);
                }

                break;
            }

            var fi = 0;
            var pi = 0;
            while (remaining > 0 && (fi < full.Count || pi < partial.Count))
            {
                // 3. Re-decided after every take: prefer a full-pallet location while the
                // remainder still needs at least one full pallet, otherwise prefer a
                // partial-pallet location. Soft fallback: if the preferred kind is exhausted
                // within this (PKM, virtual-tier) bucket, use the other kind from the same bucket
                // rather than failing or skipping ahead.
                var preferFull = remaining >= palletCapacityBoxes;
                var batch = preferFull
                    ? (fi < full.Count ? full[fi++] : partial[pi++])
                    : (pi < partial.Count ? partial[pi++] : full[fi++]);

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
