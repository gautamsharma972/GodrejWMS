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
    /// <paramref name="mfgMonth"/>) across one or more pallet positions within
    /// <paramref name="warehouseId"/> only. Existing confirmed stock and pending put-away
    /// reservations are both considered occupied for capacity scoring. When
    /// <paramref name="mfgMonth"/> is null (the Pickers' "Inward without PKM" flow - there is no
    /// PKM yet to match against), the same-SKU+PKM top-up and same-rack consolidation tiers are
    /// skipped entirely and every box lands on a genuinely empty pallet position, ranked by the
    /// same zone/velocity/season/AOP-preference scoring every other inward uses.
    /// </summary>
    Task<InwardAllocationResult> AllocateAsync(
        int materialId,
        decimal quantityBoxes,
        int? mfgMonth,
        int warehouseId,
        int stockSubtypeId = LocationSubtypeIds.Good,
        int? excludeInwardTransactionLineId = null,
        CancellationToken cancellationToken = default);
}

public sealed record InwardAllocationResult(
    AllocationStatus Status,
    decimal AllocatedQuantityBoxes,
    IReadOnlyList<InwardAllocationLine> Lines,
    string? Remarks);

public sealed record InwardAllocationLine(string LocationCode, decimal QuantityBoxes, string Reason);
