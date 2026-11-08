using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>One signed change of a stock cell (material + PKM + location). Positive adds, negative removes.</summary>
public sealed record StockChange(int MaterialId, int MfgMonth, int StockSubtypeId, PalletPosition Position, decimal DeltaBoxes);

/// <summary>Why a posting happens: the ledger entry type (<see cref="StockLedgerEntryTypes"/>), the document it belongs to and a note.</summary>
public sealed record StockPostingContext(string EntryType, string? ReferenceType = null, string? ReferenceNumber = null, string? Notes = null);

/// <summary>The single writer of <see cref="StockBatch"/> quantities for every flow that changes stock (inward post, pullout
/// deduct and correction, picker move / relocate, putaway change, adjustment, cancellation). It updates the batches,
/// writes a <see cref="StockLedgerEntry"/> per change and runs the one shared virtual-location release check after every
/// decrease, all in the caller's unit of work. Callers save (or commit) afterwards. Batch rows are never deleted; a
/// depleted cell stays at zero.</summary>
public interface IStockPostingService
{
    /// <summary>Applies the changes and returns the posting number shared by their ledger entries. Throws
    /// <see cref="InvalidOperationException"/> when a decrease would take a cell below zero.</summary>
    Task<string> ApplyAsync(StockPostingContext context, IReadOnlyList<StockChange> changes, CancellationToken cancellationToken = default);
}
