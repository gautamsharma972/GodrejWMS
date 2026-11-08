namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Runs a block of work in one database transaction. Stock changes, ledger entries and virtual-location
/// bookkeeping of one inward / pullout order must commit or roll back together. Calls nest: inside a running
/// transaction the block simply runs in it. The block may be re-run on a transient database failure, so it must
/// build its entities inside the block rather than reuse ones created before it.</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
