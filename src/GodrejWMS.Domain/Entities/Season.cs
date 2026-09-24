using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

public class Season : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The retired Season enum stored Rainy=0, Summer=1, Winter=2 - unlike the other converted
/// masters, Rainy's value is 0. Because 0 has special "auto-generate the next value" behavior on
/// a MySQL AUTO_INCREMENT column, the Season master's Id is configured as a plain, non-generated
/// key (see SeasonConfiguration) so these exact values can be reused with zero data rewriting.
/// </summary>
public static class SeasonIds
{
    public const int Rainy = 0;
    public const int Summer = 1;
    public const int Winter = 2;
}

public static class SeasonCodes
{
    public const string Rainy = "Rainy";
    public const string Summer = "Summer";
    public const string Winter = "Winter";
}
