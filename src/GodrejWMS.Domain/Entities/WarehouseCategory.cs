using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>Classification of a warehouse, e.g. RWH (regional warehouse), CFA, Plant.</summary>
public class WarehouseCategory : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
