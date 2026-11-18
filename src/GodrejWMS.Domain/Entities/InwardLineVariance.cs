using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public enum InwardVarianceType
{
    /// <summary>Boxes found damaged while unloading: they stay in the same location but are Damage stock in inventory.</summary>
    Damage = 1,

    /// <summary>Boxes that were uploaded on the inbound but did not arrive.</summary>
    Shortage = 2,

    /// <summary>Boxes that arrived but do not match what was uploaded (wrong item / pack). They leave Good stock.</summary>
    Mismatch = 3,

    /// <summary>Extra material found beyond the inbound volume (any material, any location). Posted to stock when recorded.</summary>
    Excess = 4
}

/// <summary>
/// What a supervisor recorded against a GRN while reviewing the put-away. Damage / Shortage / Mismatch account for part of the inbound
/// quantity (the put-away rows plus these records can never exceed it); Excess is additional material outside that cap.
/// The stock effect is posted through the stock posting service with reference type "GRN_VARIANCE".
/// </summary>
public class InwardLineVariance : AuditableEntity
{
    public int InwardTransactionId { get; set; }

    public InwardTransaction InwardTransaction { get; set; } = null!;

    /// <summary>The GRN line the boxes belong to; null for an Excess material that is not on the GRN.</summary>
    public int? InwardTransactionLineId { get; set; }

    public InwardTransactionLine? InwardTransactionLine { get; set; }

    /// <summary>The put-away row the boxes were taken from (Damage / Shortage / Mismatch); may be gone once the row reaches zero.</summary>
    public int? InwardPutawayId { get; set; }

    public InwardVarianceType Type { get; set; }

    public int MaterialId { get; set; }

    public Material Material { get; set; } = null!;

    /// <summary>Manufacturing month as yyyyMM.</summary>
    public int MfgMonth { get; set; }

    /// <summary>Where the boxes physically are.</summary>
    public int PalletPositionId { get; set; }

    public PalletPosition PalletPosition { get; set; } = null!;

    public decimal QuantityBoxes { get; set; }

    public string Remarks { get; set; } = string.Empty;

    public string? CreatedByUserName { get; set; }
}
