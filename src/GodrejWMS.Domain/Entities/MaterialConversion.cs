using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public enum MaterialConversionStatus
{
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Conflict = 5
}

/// <summary>A request to convert stock held under one material code into another code (the same SKU re-coded), fully or partly. Live stock is
/// changed only when the request is approved: the old code goes down and the new code up by the same boxes in the same location, PKM and stock
/// subtype, so FIFO (PKM) is preserved.</summary>
public class MaterialConversion : AuditableEntity
{
    public string ConversionNumber { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    public int FromMaterialId { get; set; }

    public Material FromMaterial { get; set; } = null!;

    public int ToMaterialId { get; set; }

    public Material ToMaterial { get; set; } = null!;

    public MaterialConversionStatus Status { get; set; } = MaterialConversionStatus.PendingApproval;

    public decimal TotalBoxes { get; set; }

    public string? ReasonText { get; set; }

    public string? RequestedByUserId { get; set; }

    public string? RequestedByUserName { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public string? ReviewedByUserId { get; set; }

    public string? ReviewedByUserName { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewComment { get; set; }

    public uint RowVersion { get; set; }

    public ICollection<MaterialConversionLine> Lines { get; set; } = new List<MaterialConversionLine>();
}

/// <summary>One stock cell (location + PKM + subtype) of the old code and the boxes taken from it, with the snapshot the approval checks against.</summary>
public class MaterialConversionLine : AuditableEntity
{
    public int MaterialConversionId { get; set; }

    public MaterialConversion MaterialConversion { get; set; } = null!;

    public int? StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    public int MfgMonth { get; set; }

    public int StockSubtypeId { get; set; }

    public decimal QuantityBoxes { get; set; }

    public decimal SystemQuantityAtRequest { get; set; }

    public uint BatchRowVersionAtRequest { get; set; }
}
