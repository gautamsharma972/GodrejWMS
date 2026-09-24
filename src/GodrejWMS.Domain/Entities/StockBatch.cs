using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// A live stock "cell" in the Inventory Master: a quantity of one <see cref="Material"/>, from one
/// manufacturing month, sitting in one <see cref="PalletPosition"/>. This is the row shape
/// produced by "Format inventory master" / "Format invent pullout-download" in the workbook.
/// A material + mfg month combination can span several batches once a pallet position fills up.
/// </summary>
public class StockBatch : AuditableEntity
{
    public int MaterialId { get; set; }

    public Material Material { get; set; } = null!;

    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    /// <summary>Manufacturing month stored as yyyyMM, e.g. 202601 for JAN|2026.</summary>
    public int MfgMonth { get; set; }

    public int StockSubtypeId { get; set; } = LocationSubtypeIds.Good;

    public LocationSubtype StockSubtype { get; set; } = null!;

    /// <summary>Stock quantity in boxes ("Total Stock in CFB" in the workbook).</summary>
    public decimal QuantityBoxes { get; set; }

    /// <summary>Optimistic-concurrency token: inward/pullout can race to mutate the same batch.</summary>
    public uint RowVersion { get; set; }

    public string MfgMonthLabel => $"{new DateOnly(MfgMonth / 100, MfgMonth % 100, 1):MMM}|{MfgMonth / 100}".ToUpperInvariant();
}
