using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>
/// Put-away engine for inward stock. It suggests/reserves pallet positions for the GRN first;
/// stock batches are written only after operator confirmation.
/// </summary>
public interface IPalletAllocationService
{
    /// <summary>
    /// Allocates <paramref name="quantityBoxes"/> of <paramref name="materialId"/> (manufactured in
    /// <paramref name="mfgMonth"/>) across one or more pallet positions. Existing confirmed stock and
    /// pending put-away reservations are both considered occupied for capacity scoring.
    /// </summary>
    Task<InwardAllocationResult> AllocateAsync(
        int materialId,
        decimal quantityBoxes,
        int mfgMonth,
        int stockSubtypeId = LocationSubtypeIds.Good,
        CancellationToken cancellationToken = default);
}

public sealed record InwardAllocationResult(
    AllocationStatus Status,
    decimal AllocatedQuantityBoxes,
    IReadOnlyList<InwardAllocationLine> Lines,
    string? Remarks);

public sealed record InwardAllocationLine(string LocationCode, decimal QuantityBoxes, string Reason);