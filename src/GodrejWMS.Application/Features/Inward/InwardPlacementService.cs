using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <inheritdoc cref="IInwardPlacementService"/>
public sealed class InwardPlacementService(
    IApplicationDbContext db,
    IPalletAllocationService allocator,
    IStockPostingService posting,
    IVirtualLocationService virtualLocations,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IInwardPlacementService
{
    public const string OverflowReason = "Virtual overflow location - no physical location was free";

    public async Task<InwardPlacementResult> PlaceLineAsync(
        InwardTransaction transaction, Material material, decimal quantityBoxes, int? mfgMonth, int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var allocation = await allocator.AllocateAsync(
            material.Id, quantityBoxes, mfgMonth, warehouseId, allowVirtualOverflow: true, cancellationToken: cancellationToken);

        var line = new InwardTransactionLine
        {
            MaterialId = material.Id,
            MfgMonth = mfgMonth,
            RequestedQuantityBoxes = quantityBoxes,
            AllocatedQuantityBoxes = allocation.AllocatedQuantityBoxes,
            Status = allocation.Status,
            Remarks = allocation.Remarks
        };
        transaction.Lines.Add(line);

        // Saved before any virtual location is taken: the location log refers to the order, and a second pallet of the same
        // line must not be handed the location the first one is about to occupy.
        await db.SaveChangesAsync(cancellationToken);

        var now = clock.UtcNow;
        var posts = mfgMonth is not null;
        var placed = new List<PlacedPutaway>();
        var changes = new List<StockChange>();

        InwardPutaway NewPutaway(PalletPosition position, decimal quantity, string? reason)
        {
            var putaway = new InwardPutaway
            {
                PalletPosition = position,
                QuantityBoxes = quantity,
                AllocationReason = reason,
                IsConfirmed = posts
            };
            if (posts)
            {
                putaway.ConfirmedAt = now;
                putaway.ConfirmedByUserId = currentUser.UserId;
                putaway.ConfirmedByUserName = currentUser.UserName;
            }

            line.Putaways.Add(putaway);
            return putaway;
        }

        foreach (var suggestion in allocation.Lines)
        {
            var position = await db.PalletPositions.Include(p => p.Rack)
                .FirstAsync(p => p.LocationCode == suggestion.LocationCode, cancellationToken);
            // Guards the location's spare room against a concurrent upload choosing it too (see PalletPosition.RowVersion).
            position.RowVersion++;
            var putaway = NewPutaway(position, suggestion.QuantityBoxes, suggestion.Reason);
            placed.Add(new PlacedPutaway(putaway, position.LocationCode, suggestion.Reason, position.IsVirtual));
            changes.Add(new StockChange(material.Id, mfgMonth ?? 0, LocationSubtypeIds.Good, position, suggestion.QuantityBoxes));
        }

        foreach (var chunk in allocation.OverflowChunks ?? [])
        {
            var position = await virtualLocations.AcquireAsync(warehouseId, transaction.Id, cancellationToken);
            var putaway = NewPutaway(position, chunk, OverflowReason);
            placed.Add(new PlacedPutaway(putaway, position.LocationCode, OverflowReason, true));
            changes.Add(new StockChange(material.Id, mfgMonth ?? 0, LocationSubtypeIds.Good, position, chunk));
        }

        if (posts && changes.Count > 0)
        {
            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.InwardPost, "GRN", transaction.ReferenceNumber), changes, cancellationToken);
            transaction.PostedAt ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new InwardPlacementResult(line, placed, allocation);
    }
}
