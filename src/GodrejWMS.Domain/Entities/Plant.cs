using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>A destination/source plant for outward pickups (e.g. "Purna", "Pune"), classified by <see cref="PlantType"/>.</summary>
public class Plant : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? PlantTypeId { get; set; }
    public PlantType? PlantType { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
