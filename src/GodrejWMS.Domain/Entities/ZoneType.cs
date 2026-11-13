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

    /// <summary>Deliberately far outside the normal auto-increment range: IDs 1-4 are the original
    /// seeded zone types, but any ID just past them (e.g. 5) may already belong to a zone type a
    /// warehouse admin created through the UI before this one was introduced, so Overflow can't
    /// safely assume the next sequential ID is free.</summary>
    public const int Overflow = 9000;

    /// <summary>A picker's "set aside as GIT" location during Inward put-away - same reserved-range
    /// reasoning as Overflow.</summary>
    public const int Git = 9001;
}

public static class ZoneTypeCodes
{
    public const string Fast = "Fast";
    public const string Reserve = "Reserve";
    public const string Seasonal = "Seasonal";
    public const string DispatchNear = "DispatchNear";
    public const string Overflow = "Overflow";
    public const string Git = "GIT";
}
