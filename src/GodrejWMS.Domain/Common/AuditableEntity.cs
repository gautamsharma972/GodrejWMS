namespace GodrejWMS.Domain.Common;

/// <summary>
/// Base type for entities that track who/when created and last modified them.
/// </summary>
public abstract class AuditableEntity
{
    public int Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? UpdatedByUserId { get; set; }
}
