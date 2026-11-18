using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>
/// Tells boxes a posted GRN never got a location for (genuinely unallocated: warehouse full, material inactive at upload) apart from
/// boxes that WERE put away and have since left inventory (picked by a pullout, moved, adjusted away). Only the first kind may be given a
/// location by Refresh Put-away; the second kind is not in the warehouse any more, so re-allocating it would create phantom stock.
/// The evidence is the stock ledger: what the GRN itself posted, net of what it took back through the GRN's own reversals and put-away changes.
/// </summary>
public static class GrnPostedQuantity
{
    /// <summary>Net boxes this GRN has posted into inventory per material and PKM (its own INWARD_POST, REVERSAL and PUTAWAY_CHANGE entries).</summary>
    public static async Task<Dictionary<(int MaterialId, int MfgMonth), decimal>> NetPostedAsync(
        IApplicationDbContext db, string grnReference, CancellationToken cancellationToken)
    {
        var rows = await db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.ReferenceType == "GRN" && e.ReferenceNumber == grnReference)
            .GroupBy(e => new { e.MaterialId, e.MfgMonth })
            .Select(g => new { g.Key.MaterialId, g.Key.MfgMonth, Net = g.Sum(e => e.QuantityBoxes) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => (r.MaterialId, r.MfgMonth), r => r.Net);
    }

    /// <summary>Boxes of the line's material + PKM that have no put-away row AND were never posted by this GRN: the only boxes a refresh may
    /// still give a location. Computed for the whole material + PKM group of the GRN.</summary>
    public static decimal UnplacedBoxes(
        IEnumerable<InwardTransactionLine> grnLines, InwardTransactionLine line,
        IReadOnlyDictionary<(int MaterialId, int MfgMonth), decimal> netPosted,
        IReadOnlyDictionary<(int MaterialId, int MfgMonth), decimal>? accountedByRecords = null)
    {
        if (line.MfgMonth is not { } month)
        {
            return Math.Max(0, line.RequestedQuantityBoxes - line.Putaways.Sum(p => p.QuantityBoxes));
        }

        // Boxes a supervisor recorded as damage / shortage / mismatch are accounted for: neither put away nor missing.
        var accounted = accountedByRecords?.GetValueOrDefault((line.MaterialId, month)) ?? 0m;
        var group = grnLines.Where(l => l.MaterialId == line.MaterialId && l.MfgMonth == month).ToList();
        var requested = group.Sum(l => l.RequestedQuantityBoxes);
        var inRows = group.Sum(l => l.Putaways.Sum(p => p.QuantityBoxes));
        var posted = netPosted.GetValueOrDefault((line.MaterialId, month));
        return Math.Max(0, Math.Min(requested - inRows - accounted, requested - posted));
    }

    /// <summary>Pullouts that picked boxes of this GRN's material + PKM from the locations the GRN put them in and no longer has a put-away row
    /// for (net of reversed pullouts), with the boxes each took. Moved or adjusted boxes carry no reference and never appear here.</summary>
    public static async Task<IReadOnlyList<(string Reference, decimal Boxes)>> PickedAgainstAsync(
        IApplicationDbContext db, InwardTransaction transaction, InwardTransactionLine line, CancellationToken cancellationToken)
    {
        if (line.MfgMonth is not { } month || transaction.PostedAt is not { } postedAt)
        {
            return [];
        }

        var stillHeld = transaction.Lines.Where(l => l.MaterialId == line.MaterialId && l.MfgMonth == month)
            .SelectMany(l => l.Putaways).Select(p => p.PalletPositionId).ToHashSet();
        var postedCells = await db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.ReferenceType == "GRN" && e.ReferenceNumber == transaction.ReferenceNumber
                && e.MaterialId == line.MaterialId && e.MfgMonth == month && e.QuantityBoxes > 0)
            .Select(e => e.PalletPositionId).Distinct().ToListAsync(cancellationToken);
        var gone = postedCells.Where(id => !stillHeld.Contains(id)).ToList();
        if (gone.Count == 0)
        {
            return [];
        }

        var picks = await db.StockLedgerEntries.AsNoTracking()
            .Where(e => e.ReferenceType == "PICK" && e.MaterialId == line.MaterialId && e.MfgMonth == month
                && gone.Contains(e.PalletPositionId) && e.CreatedAt >= postedAt)
            .GroupBy(e => e.ReferenceNumber)
            .Select(g => new { Reference = g.Key, Net = g.Sum(e => e.QuantityBoxes) })
            .ToListAsync(cancellationToken);
        return picks.Where(p => p.Reference != null && p.Net < 0)
            .OrderBy(p => p.Reference).Select(p => (p.Reference!, -p.Net)).ToList();
    }
}
