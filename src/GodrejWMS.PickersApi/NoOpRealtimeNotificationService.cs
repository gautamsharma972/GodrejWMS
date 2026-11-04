using GodrejWMS.Application.Common.Interfaces;

namespace GodrejWMS.PickersApi;

/// <summary>No-op stand-in for IRealtimeNotificationService, registered only so MediatR's
/// handler-graph validation at startup can resolve constructors for *unrelated* handlers (e.g.
/// InventoryAdjustments) that happen to live in the same Application assembly as the
/// Inward-without-PKM handlers this API actually calls. The real implementation
/// (GodrejWMS.Web.Services.RealtimeNotificationService) pushes live updates to connected Blazor
/// circuits - nothing this API does needs that, and this project never references GodrejWMS.Web
/// (see the plan), so this is a safe, inert substitute rather than a functional gap.</summary>
public sealed class NoOpRealtimeNotificationService : IRealtimeNotificationService
{
    public IDisposable Subscribe(string? userId, IReadOnlyCollection<string> roles, Func<RealtimeNotification, Task> handler) =>
        NoopDisposable.Instance;

    public Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
