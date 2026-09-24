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
    public string? PreviewJson { get; set; }
    public int RowVersion { get; set; }

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
}
