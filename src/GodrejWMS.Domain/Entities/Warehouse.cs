namespace GodrejWMS.Domain.Entities;

public class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>State and Region are derived through City (City -> State -> Region) rather than
    /// duplicated on Warehouse directly, per the org-hierarchy data model.</summary>
    public int? CityId { get; set; }
    public City? City { get; set; }

    public int? WarehouseCategoryId { get; set; }
    public WarehouseCategory? WarehouseCategory { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UserWarehouse
{
    public string UserId { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
}
