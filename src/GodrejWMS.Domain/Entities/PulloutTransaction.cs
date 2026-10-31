using GodrejWMS.Domain.Common;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Header for one pick/pullout batch — a single "inventory pullout-upload excel" submission,
/// which may contain several material/qty request lines that each get FIFO-picked by PKM.
/// </summary>
public class PulloutTransaction : AuditableEntity
{
    public int WarehouseId { get; set; } = 1;
    public Warehouse Warehouse { get; set; } = null!;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedByUserId { get; set; }
    public string? ConfirmedByUserName { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public bool IsConfirmed { get; set; } = true;

    /// <summary>The client's own order/indent number, read from the uploaded Loading Sheet's
    /// "Indent" field (when it isn't one of our own <see cref="ReferenceNumber"/>s) - lets a later
    /// upload of the same order (e.g. a growing same-day workbook re-uploaded with more sheets
    /// added) be recognized and rejected as a duplicate instead of creating a second pullout.
    /// Null when the uploaded sheet had no Indent value, or the pullout wasn't created from an
    /// upload at all.</summary>
    public string? ClientIndentNumber { get; set; }

    /// <summary>
    /// True when a saved (not yet confirmed) pullout was rejected instead. A rejected pullout never
    /// touched inventory and can no longer be refreshed or confirmed.
    /// </summary>
    public bool IsRejected { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectedByUserId { get; set; }
    public string? RejectedByUserName { get; set; }
    public string? RejectionReason { get; set; }
    public string? PreviewJson { get; set; }
    public int RowVersion { get; set; }

    // Loading Sheet header fields, captured verbatim from the uploaded Excel (if any) and carried
    // through to the downloaded Loading Sheet unchanged - this app has no domain concept of
    // transport/vehicle/driver/etc., so these are free text, never validated.
    public string? TransportName { get; set; }
    public string? VehicleNumber { get; set; }
    public string? GateNumber { get; set; }
    public string? DriverNumber { get; set; }
    public string? PlanTime { get; set; }
    public string? DateText { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? WeightText { get; set; }
    public string? VolumeText { get; set; }

    public ICollection<PulloutTransactionLine> Lines { get; set; } = new List<PulloutTransactionLine>();
}

/// <summary>
/// One material/qty request line of a <see cref="PulloutTransaction"/>. May be satisfied by
/// picks from several pallet positions/mfg months (see <see cref="Picks"/>) because FIFO
/// exhausts the oldest batch before moving to the next.
/// </summary>
public class PulloutTransactionLine
{
    public int Id { get; set; }

    public int PulloutTransactionId { get; set; }

    public PulloutTransaction PulloutTransaction { get; set; } = null!;

    public int MaterialId { get; set; }

    public Material Material { get; set; } = null!;

    public decimal RequestedQuantityBoxes { get; set; }

    public decimal PickedQuantityBoxes { get; set; }

    public AllocationStatus Status { get; set; }

    public string? Remarks { get; set; }

    /// <summary>Destination plant name as entered on this line - free text, not a foreign key to
    /// <see cref="Entities.Plant"/>, so it's never validated against the Plant Master.</summary>
    public string? PlantName { get; set; }

    /// <summary>Priority as entered in the "Priority" column of the uploaded GCPL Loading Sheet -
    /// free text (e.g. "GIT"), carried through unchanged to the downloaded Loading Sheet.</summary>
    public string? Priority { get; set; }

    public ICollection<PulloutPick> Picks { get; set; } = new List<PulloutPick>();
}

/// <summary>
/// One FIFO pick: a quantity taken from a specific pallet position/mfg-month batch to help
/// satisfy a <see cref="PulloutTransactionLine"/>. Denormalizes the location/mfg-month so the
/// pick history survives even after the source <see cref="StockBatch"/> is fully depleted and removed.
/// </summary>
public class PulloutPick
{
    public int Id { get; set; }

    public int PulloutTransactionLineId { get; set; }

    public PulloutTransactionLine PulloutTransactionLine { get; set; } = null!;

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    public int MfgMonth { get; set; }

    public decimal QuantityBoxes { get; set; }

    /// <summary>Operator justification when the FIFO-suggested source location is manually changed.</summary>
    public string? OverrideReason { get; set; }
}
