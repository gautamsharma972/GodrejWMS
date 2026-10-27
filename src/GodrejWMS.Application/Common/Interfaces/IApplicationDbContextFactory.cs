namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Creates a short-lived <see cref="IApplicationDbContext"/> independent of the shared
/// scoped one a MediatR command/query handler uses. For read-only checks that can run concurrently
/// with whatever the current request's handler is awaiting (e.g. the permission matrix lookup
/// several Blazor components may trigger at once) - EF Core forbids two concurrent operations on
/// one DbContext, so these need their own instance rather than sharing the scoped one.</summary>
public interface IApplicationDbContextFactory
{
    Task<IApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
