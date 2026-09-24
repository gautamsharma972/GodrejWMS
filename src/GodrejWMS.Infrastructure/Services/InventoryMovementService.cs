using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <inheritdoc cref="IInventoryMovementService"/>
public class InventoryMovementService(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser) : IInventoryMovementService
{
    public async Task<string?> ValidateAsync(InventoryMovementLineRequest request, CancellationToken cancellationToken = default)
    {
        var (error, _, _) = await ValidateInternalAsync(request, cancellationToken);
        return error;
    }

    public async Task<InventoryMovementLineResult> ExecuteAsync(
        InventoryMovementLineRequest request, int reasonId, CancellationToken cancellationToken = default)
    {
        var (error, sourceBatch, _) = await ValidateInternalAsync(request, cancellationToken);
        if (error is not null)
        {
            return new InventoryMovementLineResult(false, error, null);
        }

        var reasonExists = await db.MovementReasons.AnyAsync(r => r.Id == reasonId && r.IsActive, cancellationToken);
        if (!reasonExists)
        {
            return new InventoryMovementLineResult(false, "Select a valid movement reason.", null);
        }

        // sourceBatch is guaranteed non-null here - ValidateInternalAsync only returns a null
        // error alongside a non-null batch.
        sourceBatch!.QuantityBoxes -= request.QuantityBoxes;
        sourceBatch.RowVersion++;

        var destinationBatch = await db.StockBatches.FirstOrDefaultAsync(b =>
            b.MaterialId == request.MaterialId &&
            b.MfgMonth == request.MfgMonth &&
            b.PalletPositionId == request.DestinationPalletPositionId,
            cancellationToken);

        if (destinationBatch is null)
        {
            db.StockBatches.Add(new StockBatch
            {
                MaterialId = request.MaterialId,
                MfgMonth = request.MfgMonth,
                PalletPositionId = request.DestinationPalletPositionId,
                StockSubtypeId = sourceBatch.StockSubtypeId,
                QuantityBoxes = request.QuantityBoxes,
                RowVersion = 1
            });
        }
        else
        {
            destinationBatch.QuantityBoxes += request.QuantityBoxes;
            destinationBatch.RowVersion++;
        }

        var movement = new StockMovement
        {
            MovementNumber = $"IMV-{clock.UtcNow:yyyyMMdd-HHmmssfff}",
            MaterialId = request.MaterialId,
            MfgMonth = request.MfgMonth,
            StockSubtypeId = sourceBatch.StockSubtypeId,
            SourcePalletPositionId = request.SourcePalletPositionId,
            DestinationPalletPositionId = request.DestinationPalletPositionId,
            QuantityBoxes = request.QuantityBoxes,
            ReasonId = reasonId,
            PerformedByUserId = currentUser.UserId,
            PerformedByUserName = currentUser.UserName
        };
        db.StockMovements.Add(movement);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Catches both a stale RowVersion on an existing batch (DbUpdateConcurrencyException)
            // and a duplicate-key violation on the (Material, MfgMonth, Position) unique index
            // when two concurrent movements both try to create the destination's first batch row
            // at once - the second insert fails at the database level, not via a version mismatch,
            // so DbUpdateConcurrencyException alone would miss it and leak a raw SQL error instead.
            return new InventoryMovementLineResult(
                false, "This inventory was changed by another user since you loaded this screen. Please refresh and try again.", null);
        }

        return new InventoryMovementLineResult(true, null, movement);
    }

    private async Task<(string? Error, StockBatch? SourceBatch, Material? Material)> ValidateInternalAsync(
        InventoryMovementLineRequest request, CancellationToken cancellationToken)
    {
        if (request.SourcePalletPositionId == request.DestinationPalletPositionId)
        {
            return ("Source and destination locations cannot be the same.", null, null);
        }

        if (request.QuantityBoxes <= 0)
        {
            return ("Move quantity must be greater than 0.", null, null);
        }

        var sourcePosition = await db.PalletPositions.FirstOrDefaultAsync(p => p.Id == request.SourcePalletPositionId, cancellationToken);
        if (sourcePosition is null)
        {
            return ("Source location not found.", null, null);
        }

        if (!sourcePosition.IsActive)
        {
            return ($"Source location {sourcePosition.LocationCode} is inactive.", null, null);
        }

        var destinationPosition = await db.PalletPositions.FirstOrDefaultAsync(p => p.Id == request.DestinationPalletPositionId, cancellationToken);
        if (destinationPosition is null)
        {
            return ("Destination location not found.", null, null);
        }

        if (!destinationPosition.IsActive)
        {
            return ($"Destination location {destinationPosition.LocationCode} is inactive.", null, null);
        }

        var sourceBatch = await db.StockBatches.FirstOrDefaultAsync(b =>
            b.MaterialId == request.MaterialId &&
            b.MfgMonth == request.MfgMonth &&
            b.PalletPositionId == request.SourcePalletPositionId,
            cancellationToken);

        if (sourceBatch is null || sourceBatch.QuantityBoxes <= 0)
        {
            return ($"Source location {sourcePosition.LocationCode} does not have this inventory.", null, null);
        }

        if (sourceBatch.QuantityBoxes < request.QuantityBoxes)
        {
            return ($"Insufficient available quantity. Current available quantity: {sourceBatch.QuantityBoxes:0.###}.", null, null);
        }

        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken);
        if (material is null)
        {
            return ("Material not found.", null, null);
        }

        // Same rule the put-away engine and Inward reroute/confirm paths already enforce: a
        // position may only ever hold one material at a time.
        var destinationBatches = await db.StockBatches
            .Where(b => b.PalletPositionId == request.DestinationPalletPositionId && b.QuantityBoxes > 0)
            .Select(b => new { b.MaterialId, b.QuantityBoxes })
            .ToListAsync(cancellationToken);

        if (destinationBatches.Any(b => b.MaterialId != request.MaterialId))
        {
            return ("Destination location already contains a different SKU.", null, null);
        }

        var occupiedAtDestination = destinationBatches.Sum(b => b.QuantityBoxes);
        var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(destinationPosition, material);
        var freeAtDestination = effectiveCapacity - occupiedAtDestination;

        if (freeAtDestination < request.QuantityBoxes)
        {
            return ($"Destination location has insufficient capacity. Available capacity: {Math.Max(0, freeAtDestination):0.###}.", null, null);
        }

        return (null, sourceBatch, material);
    }
}
