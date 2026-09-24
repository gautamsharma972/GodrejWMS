using GodrejWMS.Domain.Common;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Audit record of one completed Inventory Movement: a relocation of an existing
/// <see cref="StockBatch"/>'s quantity from one <see cref="PalletPosition"/> to another. No
/// equivalent Stock Movement table existed in this app, so this is the minimal new entity the
/// feature needs - a StockMovement row is written only once its transaction fully commits (§17),
/// never for a partial/failed attempt. Relocation never changes the SKU/PKM identity of the stock
/// being moved (§22) - only the two location references and the moved quantity are movement-specific.
/// </summary>
public class StockMovement : AuditableEntity
{
    /// <summary>Human-readable reference, e.g. "IMV-20260913-143210" (same timestamp-based
    /// convention as <see cref="InwardTransaction.ReferenceNumber"/>).</summary>
    public string MovementNumber { get; set; } = string.Empty;

    /// <summary>Always <see cref="MovementTypes.InventoryMovement"/> today; a real column (not a
    /// hard-coded constant) so a future movement-generating feature can reuse this same table
    /// instead of duplicating it, per §18's "reuse existing movement type conventions" instruction.</summary>
    public string MovementType { get; set; } = MovementTypes.InventoryMovement;

    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    /// <summary>Manufacturing month (PKM), unchanged by the move - see class remarks.</summary>
    public int MfgMonth { get; set; }

    public int StockSubtypeId { get; set; } = LocationSubtypeIds.Good;
    public LocationSubtype StockSubtype { get; set; } = null!;

    public int SourcePalletPositionId { get; set; }
    public PalletPosition SourcePalletPosition { get; set; } = null!;

    public int DestinationPalletPositionId { get; set; }
    public PalletPosition DestinationPalletPosition { get; set; } = null!;

    public decimal QuantityBoxes { get; set; }

    public int ReasonId { get; set; }
    public MovementReason Reason { get; set; } = null!;

    public MovementStatus Status { get; set; } = MovementStatus.Completed;

    /// <summary>Denormalized at write time (same pattern as <see cref="InwardPutaway.ConfirmedByUserName"/>)
    /// since no UserId-&gt;display-name resolver exists elsewhere in this app.</summary>
    public string? PerformedByUserId { get; set; }
    public string? PerformedByUserName { get; set; }
}

public static class MovementTypes
{
    public const string InventoryMovement = "INVENTORY_MOVEMENT";
}
