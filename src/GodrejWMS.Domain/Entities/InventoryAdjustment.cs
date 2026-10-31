using GodrejWMS.Domain.Common;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Domain.Entities;

/// <summary>A physical inventory count submitted for approval. Live stock is changed only when
/// a pending request is approved.</summary>
public class InventoryAdjustment : AuditableEntity
{
    public string AdjustmentNumber { get; set; } = string.Empty;
    public int WarehouseId { get; set; } = 1;
    public Warehouse Warehouse { get; set; } = null!;
    public InventoryAdjustmentStatus Status { get; set; } = InventoryAdjustmentStatus.PendingApproval;
    /// <summary>Non-null only while pending. A unique nullable index prevents two simultaneous
    /// open requests for the same live batch; MySQL permits multiple null values.</summary>
    public int? OpenStockBatchId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? ReasonText { get; set; }
    public string? RequestedByUserId { get; set; }
    public string? RequestedByUserName { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewedByUserName { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public uint RowVersion { get; set; }
    public ICollection<InventoryAdjustmentLine> Lines { get; set; } = new List<InventoryAdjustmentLine>();
}

/// <summary>Snapshot of the counted SKU/PKM/location and its system quantity at submission.</summary>
public class InventoryAdjustmentLine : AuditableEntity
{
    public int InventoryAdjustmentId { get; set; }
    public InventoryAdjustment InventoryAdjustment { get; set; } = null!;
    public int? StockBatchId { get; set; }
    public StockBatch? StockBatch { get; set; }
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    /// <summary>
    /// Material physically found in the location when it differs from the system material.
    /// Null means this is a quantity-only adjustment.
    /// </summary>
    public int? PhysicalMaterialId { get; set; }
    public Material? PhysicalMaterial { get; set; }
    public int PalletPositionId { get; set; }
    public PalletPosition PalletPosition { get; set; } = null!;
    public int MfgMonth { get; set; }
    public int StockSubtypeId { get; set; }
    public LocationSubtype StockSubtype { get; set; } = null!;
    public decimal SystemQuantityAtCount { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal VarianceQuantity { get; set; }
    public uint BatchRowVersionAtCount { get; set; }
    /// <summary>Snapshot of an already-existing physical-material batch, if one was present when
    /// the request was submitted. These values make approval conflict-safe for both batches.</summary>
    public int? PhysicalStockBatchIdAtCount { get; set; }
    public decimal? PhysicalSystemQuantityAtCount { get; set; }
    public uint? PhysicalBatchRowVersionAtCount { get; set; }
    public decimal? ActualQuantityBeforePosting { get; set; }
    public decimal? ActualQuantityAfterPosting { get; set; }
    public decimal? ActualPhysicalQuantityBeforePosting { get; set; }
}

public static class InventoryAdjustmentReasonCodes
{
    public const string PhysicalCountVariance = "PHYSICAL_COUNT_VARIANCE";
    public const string DamagedStock = "DAMAGED_STOCK";
    public const string MissingStock = "MISSING_STOCK";
    public const string UnrecordedStock = "UNRECORDED_STOCK";
    public const string DataEntryCorrection = "DATA_ENTRY_CORRECTION";
    public const string PackagingCountConversion = "PACKAGING_COUNT_CONVERSION";
    public const string Other = "OTHER";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [PhysicalCountVariance] = "Physical count variance",
        [DamagedStock] = "Damaged stock",
        [MissingStock] = "Missing stock",
        [UnrecordedStock] = "Unrecorded stock",
        [DataEntryCorrection] = "Data-entry correction",
        [PackagingCountConversion] = "Packaging/count conversion",
        [Other] = "Other"
    };
}
