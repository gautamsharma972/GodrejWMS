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

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }
}
