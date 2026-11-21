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

    /// <summary>Hands the notification to every matching subscriber and returns at once. Delivery runs in the background on purpose: a subscriber's
    /// handler works on its own circuit (toast, bell), and when the publisher is itself running inside that same circuit (the user who just clicked
    /// Submit / Approve is also a recipient) waiting for it would stall the very action that published, leaving the page on "Submitting...".</summary>
    public Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default)
    {
        foreach (var subscription in _subscriptions.Values.Where(subscription => Matches(subscription, notification)).ToList())
        {
            _ = Task.Run(() => DeliverSafelyAsync(subscription.Handler, notification), CancellationToken.None);
        }

        return Task.CompletedTask;
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

        if (notification.Broadcast)
        {
            return true;
        }

        return notification.AudienceRoles is { Count: > 0 }
            && notification.AudienceRoles.Any(subscription.Roles.Contains);
    }

    private static async Task DeliverSafelyAsync(
        Func<RealtimeNotification, Task> handler,
        RealtimeNotification notification)
    {
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
        catch (Exception)
        {
            // Nobody waits for a delivery any more: a closed or disconnected circuit (JS interop cancelled) must not surface as an unobserved error.
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
