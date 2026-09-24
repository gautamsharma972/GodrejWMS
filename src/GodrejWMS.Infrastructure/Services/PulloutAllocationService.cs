using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>
/// FIFO pick engine: drains the oldest manufacturing month first, and within a PKM works through
/// locations in Rack -> Column -> Level order - a column is emptied from Level 1 upward
/// (A-01-01, A-01-02, ...) before moving to the next column - mirroring the put-away fill order.
/// Only good stock in active good locations is pickable; damage, expired, and hold stock remain isolated.
/// </summary>
public class PulloutAllocationService(IApplicationDbContext db) : IPulloutAllocationService
{
    public async Task<PulloutAllocationResult> AllocateAsync(
        int materialId,
        decimal quantityBoxes,
        CancellationToken cancellationToken = default,
        bool preview = false)
    {
        var remaining = quantityBoxes;
        var picks = new List<PulloutPickLine>();

        var query = db.StockBatches.AsQueryable();
        if (preview) query = query.AsNoTracking();

        var batches = await query
            .Where(b => b.MaterialId == materialId
                && b.StockSubtypeId == LocationSubtypeIds.Good
                && b.QuantityBoxes > 0
                && b.PalletPosition.IsActive
                && b.PalletPosition.LocationSubtypeId == LocationSubtypeIds.Good)
            .Include(b => b.PalletPosition)
            .OrderBy(b => b.MfgMonth)
            .ThenBy(b => b.PalletPosition.Rack.Code)
            .ThenBy(b => b.PalletPosition.Column)
            .ThenBy(b => b.PalletPosition.Level)
            .ThenBy(b => b.PalletPosition.LocationCode)
            .ToListAsync(cancellationToken);

        foreach (var batch in batches)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, batch.QuantityBoxes);
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
