using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>One put-away created for an inward line: where the boxes went and whether that location is a virtual overflow one.</summary>
public sealed record PlacedPutaway(InwardPutaway Putaway, string LocationCode, string? Reason, bool IsVirtual);

public sealed record InwardPlacementResult(InwardTransactionLine Line, IReadOnlyList<PlacedPutaway> Placed, InwardAllocationResult Allocation);

/// <summary>Allocates one inward line with the put-away engine and posts it. With a PKM the boxes go straight into inventory
/// (stock, ledger, put-away closed); without a PKM (the picker assigns it later) the allocation stays an open reservation. Boxes
/// no physical location can take are placed on virtual overflow locations. Must run inside the order's unit of work.</summary>
public interface IInwardPlacementService
{
    Task<InwardPlacementResult> PlaceLineAsync(
        InwardTransaction transaction, Material material, decimal quantityBoxes, int? mfgMonth, int warehouseId,
        CancellationToken cancellationToken = default);
}
