using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>
/// Inward is posted on upload, so a pullout uploaded before the picker has submitted the put-away can already take those boxes.
/// Such boxes are never racked: the put-away's location becomes <see cref="Label"/> (a pseudo location like GIT / NO RACK) instead of
/// the shelf cell, the row is closed for the picker, and a partial take splits the put-away. The stock itself is deducted by the
/// pullout through the posting service - this class only re-labels the inward put-away rows.
/// </summary>
public static class PutawayPickReservation
{
    public const string Label = "PICK-RESERVED";

    public static string ReasonFor(string pulloutReference) => $"Reserved for pullout {pulloutReference}";

    /// <summary>Call BEFORE the pullout's picks are deducted: the on-hand quantity read here is what the picks are taken from.
    /// Returns the number of boxes re-labelled.</summary>
    public static async Task<decimal> ReserveAsync(
        IApplicationDbContext db, string pulloutReference, IEnumerable<PulloutTransactionLine> lines, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var picked = lines
            .SelectMany(l => l.Picks.Select(p => (l.MaterialId, p.MfgMonth, p.PalletPositionId, p.QuantityBoxes)))
            .GroupBy(x => (x.MaterialId, x.MfgMonth, x.PalletPositionId))
            .ToList();
        var reason = ReasonFor(pulloutReference);
        var reservedTotal = 0m;

        foreach (var group in picked)
        {
            var (materialId, mfgMonth, positionId) = group.Key;
            var pickTotal = group.Sum(x => x.QuantityBoxes);

            var putaways = await db.InwardPutaways
                .Include(p => p.InwardTransactionLine)
                .Where(p => p.PalletPositionId == positionId
                    && p.InwardTransactionLine.MaterialId == materialId
                    && p.InwardTransactionLine.MfgMonth == mfgMonth
                    && p.InwardTransactionLine.InwardTransaction.PostedAt != null
                    && !p.InwardTransactionLine.InwardTransaction.IsRejected
                    && p.IsConfirmed && !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation == null)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken);
            if (putaways.Count == 0)
            {
                continue;
            }

            // Boxes the put-aways do not explain (older stock of the same material and PKM in the cell) are picked first;
            // only what the pick takes beyond them comes out of the not-yet-placed put-aways.
            var onHand = await db.StockBatches.AsNoTracking()
                .Where(b => b.PalletPositionId == positionId && b.MaterialId == materialId && b.MfgMonth == mfgMonth && b.StockSubtypeId == LocationSubtypeIds.Good)
                .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
            var untouched = putaways.Sum(p => p.QuantityBoxes);
            var others = Math.Max(0m, onHand - untouched);
            var remaining = Math.Min(untouched, Math.Max(0m, pickTotal - others));

            foreach (var putaway in putaways)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var take = Math.Min(putaway.QuantityBoxes, remaining);
                remaining -= take;
                reservedTotal += take;

                InwardPutaway reserved;
                if (take == putaway.QuantityBoxes)
                {
                    reserved = putaway;
                }
                else
                {
                    putaway.QuantityBoxes -= take;
                    putaway.RowVersion++;
                    reserved = new InwardPutaway
                    {
                        InwardTransactionLineId = putaway.InwardTransactionLineId,
                        PalletPositionId = putaway.PalletPositionId,
                        QuantityBoxes = take,
                        AllocationReason = putaway.AllocationReason
                    };
                    db.InwardPutaways.Add(reserved);
                }

                reserved.IsConfirmed = true;
                reserved.IsPickerPlaced = true;
                reserved.IsExcludedLocked = true;
                reserved.ExcludedLocation = Label;
                reserved.OverrideReason = reason;
                reserved.ConfirmedAt = now;
                reserved.RowVersion++;
            }
        }

        return reservedTotal;
    }

    /// <summary>The pullout was rejected. Its PICK-RESERVED put-aways go back to PENDING reservations (no stock behind them): the
    /// stock is not returned to the old cell, the user runs Refresh Put-away on the GRN and the allocator places the boxes again.
    /// Rows of a GRN that was rejected meanwhile stay as history. Returns every reserved quantity per (material, PKM, position) so the
    /// pullout's reversal does not put those boxes back into stock.</summary>
    public static async Task<Dictionary<(int MaterialId, int MfgMonth, int PositionId), decimal>> ReleaseAsync(
        IApplicationDbContext db, string pulloutReference, CancellationToken cancellationToken)
    {
        var reservedBoxes = new Dictionary<(int, int, int), decimal>();
        var reason = ReasonFor(pulloutReference);
        var rows = await db.InwardPutaways
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.InwardTransaction)
            .Where(p => p.ExcludedLocation == Label && p.OverrideReason == reason)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var line = row.InwardTransactionLine;
            if (line.MfgMonth is { } month)
            {
                var key = (line.MaterialId, month, row.PalletPositionId);
                reservedBoxes[key] = reservedBoxes.GetValueOrDefault(key) + row.QuantityBoxes;
            }

            if (line.InwardTransaction.IsRejected)
            {
                continue;
            }

            row.ExcludedLocation = null;
            row.OverrideReason = null;
            row.IsConfirmed = false;
            row.ConfirmedAt = null;
            row.ConfirmedByUserId = null;
            row.ConfirmedByUserName = null;
            row.IsPickerPlaced = false;
            row.IsExcludedLocked = false;
            row.RowVersion++;
        }

        return reservedBoxes;
    }

    /// <summary>The picker found fewer boxes than allocated (or none, when the stock is still on the inward vehicle): the shortfall goes back
    /// to stock at the pick cell, so the PICK-RESERVED put-away rows of this pullout for that cell become normal, unplaced put-aways again for
    /// up to that many boxes. A GRN that was rejected meanwhile keeps its rows as history.</summary>
    public static async Task ReleaseShortfallAsync(
        IApplicationDbContext db, string pulloutReference, int materialId, int mfgMonth, int positionId, decimal boxes,
        CancellationToken cancellationToken)
    {
        if (boxes <= 0)
        {
            return;
        }

        var reason = ReasonFor(pulloutReference);
        var rows = await db.InwardPutaways
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.InwardTransaction)
            .Where(p => p.ExcludedLocation == Label && p.OverrideReason == reason && p.PalletPositionId == positionId
                && p.InwardTransactionLine.MaterialId == materialId && p.InwardTransactionLine.MfgMonth == mfgMonth
                && !p.InwardTransactionLine.InwardTransaction.IsRejected)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (boxes <= 0)
            {
                break;
            }

            var take = Math.Min(row.QuantityBoxes, boxes);
            boxes -= take;
            InwardPutaway target;
            if (take == row.QuantityBoxes)
            {
                target = row;
            }
            else
            {
                row.QuantityBoxes -= take;
                row.RowVersion++;
                target = new InwardPutaway
                {
                    InwardTransactionLineId = row.InwardTransactionLineId,
                    PalletPositionId = row.PalletPositionId,
                    QuantityBoxes = take,
                    AllocationReason = row.AllocationReason,
                    IsConfirmed = true,
                    ConfirmedAt = row.ConfirmedAt
                };
                db.InwardPutaways.Add(target);
            }

            target.ExcludedLocation = null;
            target.OverrideReason = null;
            target.IsPickerPlaced = false;
            target.IsExcludedLocked = false;
            target.IsConfirmed = true;
            target.RowVersion++;
        }
    }
}
