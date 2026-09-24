using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class ZoneType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class ZoneTypeIds
{
    public const int Fast = 1;
    public const int Reserve = 2;
    public const int Seasonal = 3;
    public const int DispatchNear = 4;
}

public static class ZoneTypeCodes
{
    public const string Fast = "Fast";
    public const string Reserve = "Reserve";
    public const string Seasonal = "Seasonal";
    public const string DispatchNear = "DispatchNear";
}
