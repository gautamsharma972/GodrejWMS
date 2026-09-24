using GodrejWMS.Domain.Common;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Header for one goods-receipt (GRN) batch — a single "inward upload excel" submission,
/// which may contain several material/qty/mfg-month lines that each get auto put-away.
/// </summary>
public class InwardTransaction : AuditableEntity
{
    public int WarehouseId { get; set; } = 1;
    public Warehouse Warehouse { get; set; } = null!;
    public string ReferenceNumber { get; set; } = string.Empty;

    public bool IsRejected { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    public string? RejectedByUserId { get; set; }

    /// <summary>Denormalized at reject time (same pattern as <see cref="InwardPutaway.ConfirmedByUserName"/>).</summary>
    public string? RejectedByUserName { get; set; }

    public string? RejectionReason { get; set; }

    public ICollection<InwardTransactionLine> Lines { get; set; } = new List<InwardTransactionLine>();
}

/// <summary>
/// One material/qty/mfg-month line of an <see cref="InwardTransaction"/>, and the result of
/// running it through the put-away allocation engine.
/// </summary>
public class InwardTransactionLine
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }

    public InwardTransaction InwardTransaction { get; set; } = null!;

    public int MaterialId { get; set; }

    public Material Material { get; set; } = null!;

    public int MfgMonth { get; set; }

    public decimal RequestedQuantityBoxes { get; set; }

    public decimal AllocatedQuantityBoxes { get; set; }

    public AllocationStatus Status { get; set; }

    /// <summary>Human-readable note, e.g. "No pallet position with free capacity" when Status is Partial/Failed.</summary>
    public string? Remarks { get; set; }

    public ICollection<InwardPutaway> Putaways { get; set; } = new List<InwardPutaway>();
}

/// <summary>
/// One persisted put-away allocation for an inward line. Denormalizes the target location so the
/// GRN audit trail remains available even if stock is later picked or moved.
/// </summary>
public class InwardPutaway
{
    public int Id { get; set; }

    public int InwardTransactionLineId { get; set; }

    public InwardTransactionLine InwardTransactionLine { get; set; } = null!;

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    public decimal QuantityBoxes { get; set; }

    /// <summary>Human-readable explanation of why the allocation engine selected this location.</summary>
    public string? AllocationReason { get; set; }

    /// <summary>Operator-supplied justification when this put-away was manually re-routed, distinct from the engine's original <see cref="AllocationReason"/>.</summary>
    public string? OverrideReason { get; set; }

    public bool IsConfirmed { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public string? ConfirmedByUserId { get; set; }

    /// <summary>Denormalized at confirm time (same pattern as <see cref="ActivityLog.UserName"/>) so
    /// the Putaway Summary can show who confirmed this without an AspNetUsers lookup.</summary>
    public string? ConfirmedByUserName { get; set; }

    // MySQL has no native rowversion column, so this is a plain counter incremented on every
    // mutation (matching StockBatch.RowVersion); marking it a concurrency token in
    // InwardPutawayConfiguration still makes EF Core guard against a lost update.
    public uint RowVersion { get; set; }
}
