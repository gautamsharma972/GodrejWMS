using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>
/// Pick engine for outward stock. Implements FIFO by PKM:
/// the oldest manufacturing month is drained first, matching the "Format invent pullout-download"
/// sheet where a single material/qty request resolves to picks against the earliest batches.
/// </summary>
public interface IPulloutAllocationService
{
    /// <summary>
    /// Picks <paramref name="quantityBoxes"/> of <paramref name="materialId"/> from the oldest
    /// available batches first. Mutates/deletes tracked <see cref="Domain.Entities.StockBatch"/>
    /// entities via <see cref="IApplicationDbContext"/> but does not call SaveChanges — the caller
    /// owns the unit of work. With preview enabled, only detached batches are changed.
    /// </summary>
    Task<PulloutAllocationResult> AllocateAsync(
        int materialId,
        decimal quantityBoxes,
        CancellationToken cancellationToken = default,
        bool preview = false);
}

public sealed record PulloutAllocationResult(
    AllocationStatus Status,
    decimal PickedQuantityBoxes,
    IReadOnlyList<PulloutPickLine> Picks,
    string? Remarks);

public sealed record PulloutPickLine(string LocationCode, int MfgMonth, decimal QuantityBoxes);
