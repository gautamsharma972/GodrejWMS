using MediatR;

namespace GodrejWMS.Application.Common.Behaviours;

/// <summary>One gate per DI scope (= one Blazor circuit, or one API request).</summary>
public sealed class DbOperationGate
{
    internal readonly SemaphoreSlim Semaphore = new(1, 1);

    // True while the current async flow already owns the gate, so a handler that sends a nested request is not blocked by itself.
    internal readonly AsyncLocal<bool> Held = new();
}

/// <summary>
/// Blazor Server hands every component of a circuit the same scoped DbContext, and a DbContext allows only one
/// operation at a time. Any overlap (opening a record while a list is still loading, a double click, a search box
/// firing while a save runs) throws "A second operation was started on this context instance". This outermost
/// behaviour queues the requests of one scope so they run one after another, for every page and every request type,
/// without each page having to remember its own lock. Nested sends from inside a handler run straight through.
/// </summary>
public sealed class SerialisedExecutionBehaviour<TRequest, TResponse>(DbOperationGate gate)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (gate.Held.Value)
        {
            return await next(cancellationToken);
        }

        await gate.Semaphore.WaitAsync(cancellationToken);
        gate.Held.Value = true;
        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            gate.Held.Value = false;
            gate.Semaphore.Release();
        }
    }
}
