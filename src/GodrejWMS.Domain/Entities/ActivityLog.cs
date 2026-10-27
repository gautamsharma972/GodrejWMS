namespace GodrejWMS.Domain.Entities;

/// <summary>
/// Immutable operational audit record for master data and transaction changes.
/// </summary>
public class ActivityLog
{
    public int Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    /// <summary>Resolved from the changed entity where it has a direct or navigable warehouse,
    /// so scoped users can see the rows that touch their own warehouses/regions. Null for
    /// entities with no warehouse (e.g. shared master data) or where it couldn't be resolved -
    /// those rows stay Superadmin-only, the same safe default as before this column existed.</summary>
    public int? WarehouseId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }
}
