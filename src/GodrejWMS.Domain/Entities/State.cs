using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class State : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RegionId { get; set; }
    public Region Region { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<City> Cities { get; set; } = new List<City>();
}
