using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class SkuMovementType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class SkuMovementTypeIds
{
    public const int FastMoving = 1;
    public const int SlowMoving = 2;
}

public static class SkuMovementTypeCodes
{
    public const string FastMoving = "FastMoving";
    public const string SlowMoving = "SlowMoving";
}
