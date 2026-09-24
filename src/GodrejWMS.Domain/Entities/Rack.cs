using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// A physical rack in the warehouse, identified by a single letter/short code (e.g. "A", "B"),
/// per the "Nomenclature of Pallet position" sheet. A rack is a grid of <see cref="PalletPosition"/>s
/// addressed by column and level.
/// </summary>
public class Rack : AuditableEntity
{
    public int WarehouseId { get; set; } = 1;
    public Warehouse Warehouse { get; set; } = null!;
    /// <summary>Short rack code, e.g. "A". Forms the prefix of every location code in this rack.</summary>
    public string Code { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>Number of column bays in the rack (the workbook example uses 5).</summary>
    public int Columns { get; set; }

    /// <summary>Number of shelf levels, ground level up (the workbook example uses 5).</summary>
    public int Levels { get; set; }

    /// <summary>Rack shelf length in mm (warehouse standard: 2000).</summary>
    public decimal ShelfLengthMm { get; set; }

    /// <summary>Rack shelf width/depth in mm (warehouse standard: 2500).</summary>
    public decimal ShelfWidthMm { get; set; }

    /// <summary>Rack shelf height in mm (warehouse standard: 3600).</summary>
    public decimal ShelfHeightMm { get; set; }

    /// <summary>
    /// Starting value for the column term when deriving each generated
    /// <see cref="PalletPosition.DistancePriority"/> as
    /// <c>(Level - 1) * 10 + (StartingDistancePriority + Column - 1)</c>.
    /// </summary>
    public int StartingDistancePriority { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public ICollection<PalletPosition> PalletPositions { get; set; } = new List<PalletPosition>();
}
