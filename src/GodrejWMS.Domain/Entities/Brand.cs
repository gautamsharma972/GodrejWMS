using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class Brand : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Material> Materials { get; set; } = new List<Material>();
}
