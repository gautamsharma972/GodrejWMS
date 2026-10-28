using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>
/// One requested relocation: move <paramref name="QuantityBoxes"/> of the exact stock cell
/// identified by (Material, Mfg Month, Source Location) to a Destination Location. The Stock
/// Subtype travels with the source row itself - it is never supplied by the caller, so it can
/// never be changed by a move (see <see cref="IInventoryMovementService"/> remarks).
/// </summary>
public sealed record InventoryMovementLineRequest(
    int MaterialId,
    int MfgMonth,
    int SourcePalletPositionId,
    int DestinationPalletPositionId,
    decimal QuantityBoxes);

/// <summary>Outcome of executing one movement line: either a persisted <see cref="StockMovement"/>, or a business-friendly error and nothing persisted.</summary>
public sealed record InventoryMovementLineResult(bool Success, string? ErrorMessage, StockMovement? Movement);

/// <summary>
/// Inventory Movement domain service: relocates an existing, already-confirmed stock quantity
/// from one pallet position to another. Physical relocation only - SKU, PKM (Mfg Month), and Stock
/// Subtype are read from the source row and never altered (§22 of the spec this was built against:
/// "Inventory Movement must not reset or modify FIFO attributes").
///
/// Every rule below is enforced here, not in UI code (§32): location existence,
/// source-cannot-equal-destination, an active source, and sufficient source quantity. Inventory
/// Movement is the deliberate exception to put-away rules: any existing destination is allowed,
/// including inactive locations and locations of any type/subtype/zone; destination SKU mixing is
/// allowed and pallet capacity is not validated. Concurrency is handled the same way as Inward:
/// <c>StockBatch.RowVersion</c>
/// is incremented on every mutated row, so a losing concurrent move fails with
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> instead of silently
/// overbooking or over-deducting.
/// </summary>
public interface IInventoryMovementService
{
    /// <summary>Validates one line without persisting anything. Returns null if valid, or a
    /// business-friendly error message if not.</summary>
    Task<string?> ValidateAsync(InventoryMovementLineRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validates and, if valid, executes one line as its own atomic operation - deducts
    /// the source, adds to the destination, and writes the <see cref="StockMovement"/> audit row,
    /// all in one <c>SaveChangesAsync</c> call. Never throws for a business-rule failure; the
    /// caller distinguishes success/failure via <see cref="InventoryMovementLineResult.Success"/>
    /// so a multi-line batch (Move by SKU) can process every line independently instead of one
    /// failure aborting the rest.</summary>
    Task<InventoryMovementLineResult> ExecuteAsync(
        InventoryMovementLineRequest request,
        int reasonId,
        CancellationToken cancellationToken = default);
}
