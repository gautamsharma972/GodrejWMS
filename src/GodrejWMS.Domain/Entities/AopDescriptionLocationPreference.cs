using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>Maps an AOP Description to a preferred rectangle of pallet positions (columns and levels
/// between <see cref="FromPalletPosition"/> and <see cref="ToPalletPosition"/> on one rack). Inward
/// put-away runs inside that range first for any material whose <see cref="Material.AopDescriptionId"/>
/// matches, then falls back to the rest of the warehouse.</summary>
public class AopDescriptionLocationPreference : AuditableEntity
{
    public int AopDescriptionId { get; set; }
    public AopDescription AopDescription { get; set; } = null!;

    /// <summary>Deprecated. Preferences used to be keyed by Brand; the column is kept (nullable) for one
    /// release so the migration can be rolled back exactly, then it is dropped. Nothing reads it, and new
    /// rows leave it null.</summary>
    public int? BrandId { get; set; }

    public int FromPalletPositionId { get; set; }
    public PalletPosition FromPalletPosition { get; set; } = null!;

    public int ToPalletPositionId { get; set; }
    public PalletPosition ToPalletPosition { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
