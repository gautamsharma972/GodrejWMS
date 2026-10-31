namespace GodrejWMS.Application.Common.Interfaces;

public sealed record RealtimeNotification(
    Guid Id,
    string Title,
    string Message,
    string Kind,
    string? Url,
    DateTimeOffset CreatedAt,
    string? RecipientUserId = null,
    IReadOnlyCollection<string>? AudienceRoles = null,
    string? ExcludeUserId = null,
    string? Topic = null,
    bool Silent = false,
    bool Broadcast = false);

public static class RealtimeTopics
{
    public const string InventoryChanged = "inventory.changed";
    public const string InventoryAdjustmentsChanged = "inventory-adjustments.changed";
}

public interface IRealtimeNotificationService
{
    IDisposable Subscribe(
        string? userId,
        IReadOnlyCollection<string> roles,
        Func<RealtimeNotification, Task> handler);

    Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default);
}
