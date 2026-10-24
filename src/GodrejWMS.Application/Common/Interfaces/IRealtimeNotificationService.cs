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
    string? ExcludeUserId = null);

public interface IRealtimeNotificationService
{
    IDisposable Subscribe(
        string? userId,
        IReadOnlyCollection<string> roles,
        Func<RealtimeNotification, Task> handler);

    Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default);
}
