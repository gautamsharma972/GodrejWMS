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
}
