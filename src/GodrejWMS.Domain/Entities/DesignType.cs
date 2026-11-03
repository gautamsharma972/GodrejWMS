using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Master list of valid design type codes (e.g. "XOLDH") that materials are tagged with.
/// </summary>
public class DesignType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When true, materials of this design may only be put away on ground-floor locations
    /// (<see cref="PalletPosition.Level"/> 1): the allocator never suggests an upper level for them, and manual
    /// changes to an upper level need the "override ground-floor rule" permission.</summary>
    public bool RequiresGroundFloor { get; set; }
}
