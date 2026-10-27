using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class City : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StateId { get; set; }
    public State State { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
