namespace GodrejWMS.Domain.Entities;

/// <summary>One signed stock change at one location. Every flow that changes <see cref="StockBatch"/> quantities writes
/// these through the shared posting service; a move is two entries (out of the source, into the destination) sharing a
/// <see cref="PostingNumber"/>.</summary>
public class StockLedgerEntry
{
    public long Id { get; set; }

    public string PostingNumber { get; set; } = string.Empty;

    public string EntryType { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    public int MaterialId { get; set; }

    public Material Material { get; set; } = null!;

    public int MfgMonth { get; set; }

    public int StockSubtypeId { get; set; }

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    /// <summary>Positive adds stock at <see cref="PalletPositionId"/>, negative removes it.</summary>
    public decimal QuantityBoxes { get; set; }

    /// <summary>On-hand of this material + PKM at the location after the change.</summary>
    public decimal BalanceAfterBoxes { get; set; }

    public string? ReferenceType { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedByUserId { get; set; }

    public string? CreatedByUserName { get; set; }
}

public static class StockLedgerEntryTypes
{
    public const string InwardPost = "INWARD_POST";
    public const string PulloutDeduct = "PULLOUT_DEDUCT";
    public const string PutawayChange = "PUTAWAY_CHANGE";
    public const string PickChange = "PICK_CHANGE";
    public const string PickRelocate = "PICK_RELOCATE";
    public const string InventoryMovement = "INVENTORY_MOVEMENT";
    public const string Adjustment = "ADJUSTMENT";
    public const string Reversal = "REVERSAL";
    public const string StockImport = "STOCK_IMPORT";
}
