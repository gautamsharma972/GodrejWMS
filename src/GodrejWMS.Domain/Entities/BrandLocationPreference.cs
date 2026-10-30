using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// A rectangular preferred put-away range for a brand. Both endpoints must belong to the same
/// rack; the range includes every position whose column and level fall between the endpoints.
/// A brand may have multiple ranges, including ranges on different racks.
/// </summary>
public class BrandLocationPreference : AuditableEntity
{
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;

    public int FromPalletPositionId { get; set; }
    public PalletPosition FromPalletPosition { get; set; } = null!;

    public int ToPalletPositionId { get; set; }
    public PalletPosition ToPalletPosition { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
