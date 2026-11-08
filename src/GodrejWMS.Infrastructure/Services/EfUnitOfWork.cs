using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>Transaction runner on the scoped <see cref="AppDbContext"/>. Goes through the execution strategy because the
/// MySQL provider is configured with EnableRetryOnFailure, which forbids a bare user-initiated transaction.</summary>
public sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        // A nested call just joins the running transaction (the outer call cleans up after a failure).
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken);
        }

        // The in-memory test provider has no transactions: a failed block only has to leave nothing pending in the tracker.
        if (!db.Database.IsRelational())
        {
            try
            {
                return await action(cancellationToken);
            }
            catch
            {
                db.ChangeTracker.Clear();
                throw;
            }
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // READ COMMITTED: takes record locks only (no gap / next-key locks), so concurrent uploads taking free virtual
            // locations with SKIP LOCKED do not deadlock on index gaps. Lost updates are still caught by the row-version tokens.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var result = await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // Nothing of the failed attempt may leak into the shared, long-lived context.
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync<bool>(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
}
