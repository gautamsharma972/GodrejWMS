using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public enum CycleCountBatchStatus
{
    Scheduled = 1,
    InProgress = 2,
    Extended = 3,
    PartiallyClosed = 4,
    PendingReview = 5,
    Closed = 6,
    Dropped = 7
}

public enum CycleCountRackStatus
{
    Open = 1,
    Submitted = 2,
    /// <summary>Left out when the batch was partially closed; its uncounted cards are not applied to stock.</summary>
    Excluded = 3
}

public enum CycleCountCardState
{
    Pending = 1,
    /// <summary>The counter confirmed the system quantity (OK).</summary>
    Verified = 2,
    /// <summary>The counter saved a different physical count (material, PKM and / or quantity).</summary>
    Corrected = 3
}

public enum CycleCountReviewStatus
{
    PendingReview = 1,
    Reviewed = 2
}

/// <summary>A scheduled physical count of selected racks of one warehouse. While it is open (from <see cref="ScheduleFrom"/> until it is
/// closed or dropped) new Inward and Pullout requests are refused for the warehouse. Stock changes only when a rack is submitted.</summary>
public class CycleCountBatch : AuditableEntity
{
    public string BatchNumber { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    public Warehouse Warehouse { get; set; } = null!;

    public DateTimeOffset ScheduleFrom { get; set; }

    public DateTimeOffset ScheduleTo { get; set; }

    public DateTimeOffset? ActualStartAt { get; set; }

    public DateTimeOffset? ActualEndAt { get; set; }

    public CycleCountBatchStatus Status { get; set; } = CycleCountBatchStatus.Scheduled;

    public string? Remarks { get; set; }

    public string? DropReason { get; set; }

    /// <summary>True once the count cards (the system-stock snapshot) have been generated for every selected rack.</summary>
    public bool CardsGenerated { get; set; }

    /// <summary>When true the Inward / Pullout freeze ends by itself once Schedule To has passed (the batch itself stays open until closed).</summary>
    public bool AutoReleaseFreezeAtScheduleEnd { get; set; }

    public string? CreatedByUserName { get; set; }

    public string? UpdatedByUserName { get; set; }

    public uint RowVersion { get; set; }

    public ICollection<CycleCountBatchRack> Racks { get; set; } = new List<CycleCountBatchRack>();

    public ICollection<CycleCountCard> Cards { get; set; } = new List<CycleCountCard>();

    public ICollection<CycleCountHistory> History { get; set; } = new List<CycleCountHistory>();

    public static bool IsOpen(CycleCountBatchStatus status) => status is not (CycleCountBatchStatus.Closed or CycleCountBatchStatus.Dropped);
}

/// <summary>One rack (by its code, e.g. "A", which can span several rack rows) selected for a batch. Submission is per rack.</summary>
public class CycleCountBatchRack : AuditableEntity
{
    public int CycleCountBatchId { get; set; }

    public CycleCountBatch CycleCountBatch { get; set; } = null!;

    public string RackCode { get; set; } = string.Empty;

    public CycleCountRackStatus Status { get; set; } = CycleCountRackStatus.Open;

    /// <summary>The counters chose to count damaged stock of this rack too (default: Good stock only).</summary>
    public bool IncludesDamageStock { get; set; }

    public string? SubmittedByUserId { get; set; }

    public string? SubmittedByUserName { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public uint RowVersion { get; set; }
}

/// <summary>One location + material + PKM to count. The system figures are the snapshot taken when the cards were generated; the physical
/// figures stay empty until the counter acts. A location with no stock gets one card with no system material so found stock can be recorded.</summary>
public class CycleCountCard : AuditableEntity
{
    public int CycleCountBatchId { get; set; }

    public CycleCountBatch CycleCountBatch { get; set; } = null!;

    public string RackCode { get; set; } = string.Empty;

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    /// <summary>Rack code, column, level, material: the one ordering used by web and mobile (numeric parts zero-padded).</summary>
    public string SequenceKey { get; set; } = string.Empty;

    public int? SystemMaterialId { get; set; }

    public Material? SystemMaterial { get; set; }

    public int? SystemMfgMonth { get; set; }

    public decimal SystemQuantityBoxes { get; set; }

    /// <summary>Good (1) or Damage (2).</summary>
    public int SystemStockSubtypeId { get; set; } = LocationSubtypeIds.Good;

    public int? PhysicalMaterialId { get; set; }

    public Material? PhysicalMaterial { get; set; }

    public int? PhysicalMfgMonth { get; set; }

    public decimal? PhysicalQuantityBoxes { get; set; }

    public int? PhysicalStockSubtypeId { get; set; }

    public decimal? DifferenceBoxes { get; set; }

    public CycleCountCardState State { get; set; } = CycleCountCardState.Pending;

    /// <summary>Stock found that the system did not list at this location (added by the counter).</summary>
    public bool IsExtra { get; set; }

    /// <summary>Left uncounted when the batch was partially closed; never applied to stock.</summary>
    public bool IsExcluded { get; set; }

    public string? CountedByUserId { get; set; }

    public string? CountedByUserName { get; set; }

    public DateTimeOffset? CountedAt { get; set; }

    public string? SubmittedByUserId { get; set; }

    public string? SubmittedByUserName { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public uint RowVersion { get; set; }
}

/// <summary>Who did what to a batch or card, with the previous and the new value.</summary>
public class CycleCountHistory
{
    public long Id { get; set; }

    public int CycleCountBatchId { get; set; }

    public CycleCountBatch CycleCountBatch { get; set; } = null!;

    public int? CycleCountCardId { get; set; }

    public string? RackCode { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? PreviousValue { get; set; }

    public string? NewValue { get; set; }

    public string? Reason { get; set; }

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

public static class CycleCountActions
{
    public const string Scheduled = "SCHEDULED";
    public const string Extended = "EXTENDED";
    public const string Reduced = "REDUCED";
    public const string Started = "STARTED";
    public const string PartiallyClosed = "PARTIALLY_CLOSED";
    public const string Dropped = "DROPPED";
    public const string Closed = "CLOSED";
    public const string AutoClosed = "AUTO_CLOSED";
    public const string Verified = "VERIFIED";
    public const string Corrected = "CORRECTED";
    public const string Reopened = "REOPENED";
    public const string FoundStockAdded = "FOUND_STOCK_ADDED";
    public const string Submitted = "SUBMITTED";
    public const string Reviewed = "REVIEWED";
    public const string SnapshotDrift = "SNAPSHOT_DRIFT";
    public const string DamageCountingEnabled = "DAMAGE_COUNTING_ENABLED";
}

/// <summary>The Head Supervisor's verification of one submitted rack. Never changes stock.</summary>
public class CycleCountReview : AuditableEntity
{
    public int CycleCountBatchId { get; set; }

    public CycleCountBatch CycleCountBatch { get; set; } = null!;

    public string RackCode { get; set; } = string.Empty;

    public CycleCountReviewStatus Status { get; set; } = CycleCountReviewStatus.PendingReview;

    public string? ReviewedByUserId { get; set; }

    public string? ReviewedByUserName { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewRemarks { get; set; }
}
