using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Master list of pallet-position/stock subtypes (Good, Damage, Expire, Hold). A fixed set for
/// now — display name, description, sort order, and active state are editable, but rows are
/// never created or deleted through the UI. Seeded with Ids matching the historical enum values
/// (Good=1, Damage=2, Expire=3, Hold=4) so existing stored data needed no migration.
/// </summary>
public class LocationSubtype : AuditableEntity
{
    /// <summary>Stable code business logic can reference regardless of DisplayName edits — see <see cref="LocationSubtypeIds"/>.</summary>
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Well-known, stable <see cref="LocationSubtype"/> ids for business logic that needs to compare against a specific subtype without a lookup.</summary>
public static class LocationSubtypeIds
{
    public const int Good = 1;
    public const int Damage = 2;
    public const int Expire = 3;
    public const int Hold = 4;
}

/// <summary>Well-known, stable <see cref="LocationSubtype"/> codes, matching the DisplayName-independent seed data.</summary>
public static class LocationSubtypeCodes
{
    public const string Good = "Good";
    public const string Damage = "Damage";
    public const string Expire = "Expire";
    public const string Hold = "Hold";
}
