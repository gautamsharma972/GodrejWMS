using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster;

/// <summary>
/// Where stock is in its physical flow, derived for display only. The books move at upload time (an inward upload posts stock at once, a
/// pullout upload deducts it at once); this tells a reader which boxes are really on the racks:
/// <list type="bullet">
/// <item><b>GIT-IN</b>: posted by an inward upload, but the picker has not confirmed that put-away location yet (still part of the book stock).</item>
/// <item><b>IN-STOCK</b>: everything else in the books (put-away confirmed, opening stock, moves, adjustments, counts ...).</item>
/// <item><b>GIT-OUT</b>: already deducted by a pullout upload, but the picker has not confirmed that pick yet (so the boxes are still on the rack;
/// NOT part of the book stock).</item>
/// </list>
/// Nothing here writes to stock, the ledger or any order; it only reads.
/// </summary>
public static class StockFlowStatus
{
    public const string InStock = "IN-STOCK";
    public const string GitIn = "GIT-IN";
    public const string GitOut = "GIT-OUT";

    public static readonly IReadOnlyList<string> All = [InStock, GitIn, GitOut];

    /// <summary>A pick the picker has not confirmed yet: boxes still on the rack that the books no longer count.</summary>
    public sealed record OpenPick(
        int PickId, int MaterialId, int MfgMonth, int PalletPositionId, decimal QuantityBoxes);

    /// <summary>Boxes of posted, non-rejected inward orders at a location (per material + PKM) whose put-away the picker has not submitted and
    /// nobody has locked. Capped at the cell's book quantity by the caller: moves, picks and adjustments can have taken some of them since.</summary>
    public static async Task<Dictionary<(int MaterialId, int MfgMonth, int PalletPositionId), decimal>> PendingPutawayByCellAsync(
        IApplicationDbContext db, int? warehouseId, CancellationToken cancellationToken)
    {
        var query = db.InwardPutaways.AsNoTracking()
            .Where(p => p.IsConfirmed && !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation == null && p.QuantityBoxes > 0
                && p.InwardTransactionLine.MfgMonth != null
                && !p.InwardTransactionLine.InwardTransaction.IsRejected
                && p.InwardTransactionLine.InwardTransaction.PostedAt != null);
        if (warehouseId is { } wh)
        {
            query = query.Where(p => p.InwardTransactionLine.InwardTransaction.WarehouseId == wh);
        }

        var rows = await query
            .Select(p => new { p.InwardTransactionLine.MaterialId, Mfg = p.InwardTransactionLine.MfgMonth!.Value, p.PalletPositionId, p.QuantityBoxes })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => (r.MaterialId, r.Mfg, r.PalletPositionId)).ToDictionary(g => g.Key, g => g.Sum(r => r.QuantityBoxes));
    }

    /// <summary>Picks of uploaded, non-rejected pullouts that the picker has not confirmed (and whose pick-list is not confirmed).</summary>
    public static async Task<List<OpenPick>> OpenPicksAsync(IApplicationDbContext db, int? warehouseId, CancellationToken cancellationToken)
    {
        var query = db.PulloutPicks.AsNoTracking()
            .Where(p => p.PickedAt == null && p.QuantityBoxes > 0
                && p.PulloutTransactionLine.PulloutTransaction.IsConfirmed
                && !p.PulloutTransactionLine.PulloutTransaction.IsRejected
                && p.PulloutTransactionLine.PulloutTransaction.UploadHash != null
                && p.PulloutTransactionLine.PulloutTransaction.ConfirmedAt == null);
        if (warehouseId is { } wh)
        {
            query = query.Where(p => p.PulloutTransactionLine.PulloutTransaction.WarehouseId == wh);
        }

        return await query
            .Select(p => new OpenPick(p.Id, p.PulloutTransactionLine.MaterialId, p.MfgMonth, p.PalletPositionId, p.QuantityBoxes))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Splits a book quantity into (in stock, git-in) given the pending put-away boxes of the same cell.</summary>
    public static (decimal InStock, decimal GitIn) Split(decimal bookQuantity, decimal pendingPutaway)
    {
        var gitIn = Math.Max(0m, Math.Min(bookQuantity, pendingPutaway));
        return (bookQuantity - gitIn, gitIn);
    }
}
