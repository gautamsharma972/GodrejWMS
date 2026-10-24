using System.Collections.Concurrent;
using GodrejWMS.Application.Common.Interfaces;

namespace GodrejWMS.Web.Services;

public sealed class RealtimeNotificationService : IRealtimeNotificationService
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();

    public IDisposable Subscribe(
        string? userId,
        IReadOnlyCollection<string> roles,
        Func<RealtimeNotification, Task> handler)
    {
        var id = Guid.NewGuid();
        _subscriptions[id] = new Subscription(userId, roles.ToHashSet(StringComparer.OrdinalIgnoreCase), handler);
        return new SubscriptionHandle(() => _subscriptions.TryRemove(id, out _));
    }

    public async Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default)
    {
        var deliveries = _subscriptions.Values
            .Where(subscription => Matches(subscription, notification))
            .Select(subscription => DeliverSafelyAsync(subscription.Handler, notification, cancellationToken));
        await Task.WhenAll(deliveries);
    }

    private static bool Matches(Subscription subscription, RealtimeNotification notification)
    {
        if (!string.IsNullOrWhiteSpace(notification.ExcludeUserId)
            && notification.ExcludeUserId == subscription.UserId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(notification.RecipientUserId))
        {
            return notification.RecipientUserId == subscription.UserId;
        }

        return notification.AudienceRoles is { Count: > 0 }
            && notification.AudienceRoles.Any(subscription.Roles.Contains);
    }

    private static async Task DeliverSafelyAsync(
        Func<RealtimeNotification, Task> handler,
        RealtimeNotification notification,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await handler(notification);
        }
        catch (ObjectDisposedException)
        {
            // A Blazor circuit can close between snapshotting subscriptions and delivery.
        }
        catch (InvalidOperationException)
        {
            // The circuit is no longer available; its subscription is removed during disposal.
        }
    }

    private sealed record Subscription(
        string? UserId,
        HashSet<string> Roles,
        Func<RealtimeNotification, Task> Handler);

    private sealed class SubscriptionHandle(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
