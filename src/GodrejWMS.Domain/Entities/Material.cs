using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Material (SKU) master data, sourced from the "MaterialMaster" sheet of the warehouse
/// logic workbook. One row per sellable material/design/pack combination.
/// </summary>
public class Material : AuditableEntity
{
    /// <summary>The business key printed on cartons and used in every upload/download format ("Material Code").</summary>
    public long MaterialNumber { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Mould/design code, e.g. "XOLDH".</summary>
    public string DesignType { get; set; } = string.Empty;

    /// <summary>Optional variant/characteristic code, e.g. "SHAC1".</summary>
    public string? CharacteristicValue { get; set; }

    /// <summary>Units packed per box/case ("PK Size").</summary>
    public int PackSize { get; set; }

    public decimal MrpPrice { get; set; }

    public decimal LengthMm { get; set; }

    public decimal WidthMm { get; set; }

    public decimal HeightMm { get; set; }

    /// <summary>Net weight of a single unit, in KG.</summary>
    public decimal NetWeightKg { get; set; }

    /// <summary>Gross weight of a single unit, in KG.</summary>
    public decimal GrossWeightKg { get; set; }

    /// <summary>Boxes that fit on one pallet for this material (warehouse standard is 40).</summary>
    public int PalletCapacityBoxes { get; set; } = 40;

    public int MovementTypeId { get; set; } = SkuMovementTypeIds.SlowMoving;
    public SkuMovementType MovementType { get; set; } = null!;

    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;

    /// <summary>Optional put-away preference. By default (<see cref="RequirePreferredZone"/> false)
    /// the allocation engine scores a matching zone slightly better but never restricts placement
    /// to it. When <see cref="RequirePreferredZone"/> is true, this becomes a hard constraint -
    /// only positions in this zone are ever eligible, for automatic allocation and manual reroute
    /// alike.</summary>
    public int? PreferredZoneTypeId { get; set; }
    public ZoneType? PreferredZoneType { get; set; }

    /// <summary>Promotes <see cref="PreferredZoneTypeId"/> from a soft scoring nudge to a hard
    /// eligibility gate. Meaningless (and validated as such) unless a preferred zone is also set.</summary>
    public bool RequirePreferredZone { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Box volume in mm^3 (Length x Width x Height). Kept in sync by <see cref="RecalculateDerivedFields"/>.</summary>
    public decimal VolumeMm3 { get; private set; }

    /// <summary>Weight of one full box in KG (Gross Weight x Pack Size), matching the workbook's formula.</summary>
    public decimal BoxWeightKg { get; private set; }

    /// <summary>Weight of one full pallet in KG based on this material's pallet capacity.</summary>
    public decimal PalletWeightKg { get; private set; }

    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();

    public void RecalculateDerivedFields()
    {
        VolumeMm3 = LengthMm * WidthMm * HeightMm;
        BoxWeightKg = GrossWeightKg * PackSize;
        PalletWeightKg = BoxWeightKg * PalletCapacityBoxes;
    }
}
