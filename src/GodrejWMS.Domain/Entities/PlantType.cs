using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class PlantType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
