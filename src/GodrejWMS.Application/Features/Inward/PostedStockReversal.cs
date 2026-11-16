using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>What a GRN put-away wants to take back out of one stock cell.</summary>
public sealed record StockReversalItem(int MaterialId, int MfgMonth, PalletPosition Position, decimal Quantity);

/// <summary>The part of a reversal that can actually be done, and what could not.</summary>
public sealed record StockReversalPlan(
    IReadOnlyList<StockChange> Changes,
    decimal ReversedBoxes,
    decimal ShortfallBoxes,
    IReadOnlyDictionary<(int MaterialId, int MfgMonth), decimal> ReversedByMaterial);

/// <summary>
/// Plans the take-back of stock a GRN posted to its locations. Boxes picked, moved or reloaded since the put-away are no
/// longer in the cell, so each cell gives back at most what it holds; the rest is reported as a shortfall instead of
/// blocking the operation.
/// </summary>
public static class PostedStockReversal
{
    public static async Task<StockReversalPlan> PlanAsync(
        IApplicationDbContext db, IReadOnlyCollection<StockReversalItem> items, CancellationToken cancellationToken)
    {
        var changes = new List<StockChange>();
        var reversedByMaterial = new Dictionary<(int, int), decimal>();
        decimal shortfall = 0;
        if (items.Count == 0)
        {
            return new StockReversalPlan(changes, 0, 0, reversedByMaterial);
        }

        var materialIds = items.Select(i => i.MaterialId).Distinct().ToList();
        var positionIds = items.Select(i => i.Position.Id).Distinct().ToList();
        var batches = await db.StockBatches.AsNoTracking()
            .Where(b => materialIds.Contains(b.MaterialId) && positionIds.Contains(b.PalletPositionId)
                && b.StockSubtypeId == LocationSubtypeIds.Good)
            .ToListAsync(cancellationToken);

        foreach (var cell in items.GroupBy(i => (i.MaterialId, i.MfgMonth, PositionId: i.Position.Id)))
        {
            var wanted = cell.Sum(i => i.Quantity);
            var onHand = batches
                .Where(b => b.MaterialId == cell.Key.MaterialId && b.MfgMonth == cell.Key.MfgMonth && b.PalletPositionId == cell.Key.PositionId)
                .Sum(b => b.QuantityBoxes);
            var take = Math.Min(wanted, Math.Max(0, onHand));
            shortfall += wanted - take;
            if (take <= 0)
            {
                continue;
            }

            changes.Add(new StockChange(cell.Key.MaterialId, cell.Key.MfgMonth, LocationSubtypeIds.Good, cell.First().Position, -take));
            var key = (cell.Key.MaterialId, cell.Key.MfgMonth);
            reversedByMaterial[key] = reversedByMaterial.GetValueOrDefault(key) + take;
        }

        return new StockReversalPlan(changes, changes.Sum(c => -c.DeltaBoxes), shortfall, reversedByMaterial);
    }
}
