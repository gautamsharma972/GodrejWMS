using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// A storage location within a <see cref="Rack"/>, addressed as "{RackCode}-{Column:00}-{Level:00}"
/// (e.g. "A-01-01"), matching the "Nomenclature of Pallet position" sheet. Also carries the
/// secondary flat physical label used on floor signage (e.g. "A1", "B14").
/// </summary>
public class PalletPosition : AuditableEntity
{
    public int RackId { get; set; }

    public Rack Rack { get; set; } = null!;

    /// <summary>1-based column/bay number.</summary>
    public int Column { get; set; }

    /// <summary>1-based level number, 1 = ground level.</summary>
    public int Level { get; set; }

    /// <summary>Computed "{Rack}-{Column:00}-{Level:00}" code, stored for fast lookup/uniqueness.</summary>
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>Secondary flat physical label painted on the rack (e.g. "A1"), if assigned.</summary>
    public string? FlatLabel { get; set; }

    public int LocationTypeId { get; set; } = LocationTypeIds.Rack;

    public LocationType LocationType { get; set; } = null!;

    public int LocationSubtypeId { get; set; } = LocationSubtypeIds.Good;

    public LocationSubtype LocationSubtype { get; set; } = null!;

    public int ZoneTypeId { get; set; } = ZoneTypeIds.Reserve;

    public ZoneType ZoneType { get; set; } = null!;

    /// <summary>Lower values are closer/easier to pick from.</summary>
    public int DistancePriority { get; set; } = 100;

    public int MaxPallets { get; set; } = 2;

    public bool IsActive { get; set; } = true;

    // MySQL has no native rowversion column, so this is a plain counter that put-away reservation
    // explicitly increments whenever it reserves capacity against this position - purely to give
    // EF Core something to guard with. Two concurrent inward submissions racing for the same
    // position's spare capacity will have one lose with DbUpdateConcurrencyException instead of
    // silently overbooking it; see SubmitInwardHandler's retry loop.
    public uint RowVersion { get; set; }

    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();

    public static string BuildLocationCode(string rackCode, int column, int level) =>
        $"{rackCode}-{column:00}-{level:00}";
}
