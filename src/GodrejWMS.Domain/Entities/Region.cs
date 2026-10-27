using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class Region : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<State> States { get; set; } = new List<State>();
}
