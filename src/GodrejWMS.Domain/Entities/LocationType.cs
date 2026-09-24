using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class LocationType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class LocationTypeIds
{
    public const int Rack = 1;
    public const int Pallet = 2;
    public const int Floor = 3;
    public const int Yard = 4;
}

public static class LocationTypeCodes
{
    public const string Rack = "Rack";
    public const string Pallet = "Pallet";
    public const string Floor = "Floor";
    public const string Yard = "Yard";
}
