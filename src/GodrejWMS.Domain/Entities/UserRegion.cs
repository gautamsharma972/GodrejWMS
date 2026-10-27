namespace GodrejWMS.Domain.Entities;

/// <summary>Grants a Logistic Manager access to every warehouse within this region (via
/// Warehouse -> City -> State -> Region), without needing a row per warehouse.</summary>
public class UserRegion
{
    public string UserId { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public Region Region { get; set; } = null!;
}
