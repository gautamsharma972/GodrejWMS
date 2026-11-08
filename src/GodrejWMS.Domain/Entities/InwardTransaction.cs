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

    /// <summary>SHA-256 of the warehouse, header and normalised lines of the upload. An upload with the same hash as a non-rejected
    /// order of the last 24 hours is refused, so re-uploading the same file cannot post it twice.</summary>
    public string? UploadHash { get; set; }

    /// <summary>When the order's stock was posted to inventory (upload time for orders with a PKM).</summary>
    public DateTimeOffset? PostedAt { get; set; }

    public bool IsRejected { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    public string? RejectedByUserId { get; set; }

    /// <summary>Denormalized at reject time (same pattern as <see cref="InwardPutaway.ConfirmedByUserName"/>).</summary>
    public string? RejectedByUserName { get; set; }

    public string? RejectionReason { get; set; }

    // Captured verbatim from a multi-sheet Unloading Sheet workbook upload (D3/D2/F5 respectively)
    // - free text, never validated. Null for manual entry and the plain single-sheet upload. Used
    // to recognize a re-uploaded sheet as an order already received today (see
    // ImportInwardWorkbookHandler), the same role PulloutTransaction's header fields play for Pullout.
    public string? VehicleNumber { get; set; }
    public string? LrNumber { get; set; }
    public string? ReceivedFrom { get; set; }

    /// <summary>True only for a GRN submitted through the Pickers' "Inward without PKM" flow -
    /// every line's <see cref="InwardTransactionLine.MfgMonth"/> is null until someone assigns a
    /// real PKM. Used to show the receiving/confirming screens a "PKM needed" flag, and to scope
    /// the Pickers-only history view to just these (still-pending) GRNs.</summary>
    /// <summary>The first seven rows of the uploaded Unloading Sheet (invoice, LR/vehicle, transport,
    /// dock, dates...), stored as JSON so the GRN Excel download can repeat them at the top exactly as
    /// the client typed them. See <c>InwardUploadHeaderBlock</c>. Null for manual entry, the plain
    /// single-sheet upload, and GRNs created before this was captured.</summary>
    public string? UploadHeaderJson { get; set; }

    public bool IsPkmless { get; set; }

    public ICollection<InwardTransactionLine> Lines { get; set; } = new List<InwardTransactionLine>();

    /// <summary>Uploaded lines that could not become a <see cref="InwardTransactionLine"/> (the material code is not
    /// in the Material Master, or the sheet row's code could not be read). Kept so the GRN still accounts for every
    /// box that was uploaded - they count towards the inbound volume and show as not allocated.</summary>
    public ICollection<InwardUnsavedLine> UnsavedLines { get; set; } = new List<InwardUnsavedLine>();
}

/// <summary>One uploaded line that was not saved as a GRN line. See <see cref="InwardTransaction.UnsavedLines"/>.</summary>
public class InwardUnsavedLine
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }

    public InwardTransaction InwardTransaction { get; set; } = null!;

    /// <summary>The code exactly as uploaded (text, because an unreadable row has no valid number).</summary>
    public string CodeText { get; set; } = string.Empty;

    /// <summary>The name typed next to the code in the uploaded sheet, if there was one.</summary>
    public string? Description { get; set; }

    public decimal QuantityBoxes { get; set; }

    public InwardUnsavedLineKind Kind { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Row number in the uploaded sheet, when the line came from a workbook upload.</summary>
    public int? SourceRow { get; set; }

    /// <summary>The line's Mfg Month (yyyymm) as uploaded - kept so the line can be allocated later, once its material
    /// exists in the master, without asking for the PKM again. Null for no-PKM uploads and for lines saved before this
    /// was recorded.</summary>
    public int? MfgMonth { get; set; }
}

public enum InwardUnsavedLineKind
{
    /// <summary>The material code is not in the Material Master.</summary>
    UnknownMaterial = 0,

    /// <summary>The sheet row's code was not a valid material number, so the row could not be read.</summary>
    UnreadableRow = 1
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

    /// <summary>Null only for a line submitted through the Pickers' "Inward without PKM" flow
    /// (see <see cref="InwardTransaction.IsPkmless"/>) - every other line always has a real value.
    /// <see cref="InwardPutawayConfirmationLogic"/> refuses to confirm a null-PKM line into real
    /// <see cref="StockBatch"/> inventory until a real PKM is assigned.</summary>
    public int? MfgMonth { get; set; }

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

    /// <summary>True when the Picker chose this location on the floor (Picker Put-away screen). Such a location may hold
    /// other materials/PKMs and may already be full, so confirmation skips the single-material, PKM-mixing and capacity
    /// checks that guard system-suggested locations.</summary>
    public bool IsPickerPlaced { get; set; }

    /// <summary>"GIT" or "NO RACK" when the Picker set the goods aside instead of racking them. Such a put-away is closed
    /// (<see cref="IsConfirmed"/>) without ever creating stock, and <see cref="PalletPositionId"/> only remembers the
    /// location the system had suggested.</summary>
    public string? ExcludedLocation { get; set; }

    /// <summary>Set when the GRN is confirmed - by a supervisor, or for a GRN posted on upload by the picker's own Confirm (which
    /// locks every row of the GRN): the Picker can no longer change the location, PKM or GIT / NO RACK entry. Until then a GRN that
    /// holds such entries is not "confirmed", even when every location is closed.</summary>
    public bool IsExcludedLocked { get; set; }

    /// <summary>The quantity the system suggested for this location, remembered the first time the Picker changes it. The Picker's
    /// entries for one material on a GRN must add up to the total the system allocated; null means the quantity is untouched.</summary>
    public decimal? SuggestedQuantityBoxes { get; set; }

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
