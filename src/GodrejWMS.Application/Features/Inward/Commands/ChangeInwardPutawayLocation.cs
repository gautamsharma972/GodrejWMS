using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Re-routes a pending (unconfirmed) put-away reservation to a different pallet position, before
/// the operator confirms it. Confirmed put-aways already have stock booked and can no longer be
/// moved this way. A reason is mandatory since this always overrides the engine's own recommendation.
/// </summary>
public sealed record ChangeInwardPutawayLocationCommand(
    int PutawayId,
    int NewPalletPositionId,
    string? Reason = null,
    decimal? QuantityBoxes = null) : IRequest<InwardResultDto>;

public sealed class ChangeInwardPutawayLocationValidator : AbstractValidator<ChangeInwardPutawayLocationCommand>
{
    public ChangeInwardPutawayLocationValidator()
    {
        RuleFor(x => x.PutawayId).GreaterThan(0);
        RuleFor(x => x.NewPalletPositionId).GreaterThan(0);
        RuleFor(x => x.QuantityBoxes)
            .GreaterThan(0)
            .Must(quantity => !quantity.HasValue || decimal.IsInteger(quantity.Value))
            .WithMessage("Quantity must be a positive whole number.")
            .When(x => x.QuantityBoxes.HasValue);
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("A reason is required when overriding the recommended put-away location.")
            .MaximumLength(250);
    }
}

public sealed class ChangeInwardPutawayLocationHandler(IApplicationDbContext db)
    : IRequestHandler<ChangeInwardPutawayLocationCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(ChangeInwardPutawayLocationCommand request, CancellationToken cancellationToken)
    {
        var putaway = await db.InwardPutaways
            .Include(p => p.InwardTransactionLine)
                .ThenInclude(l => l.Putaways)
            .Include(p => p.PalletPosition)
            .FirstOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);

        if (putaway.IsConfirmed)
        {
            throw new InvalidOperationException("This put-away has already been confirmed and can no longer be changed.");
        }

        var transactionId = putaway.InwardTransactionLine.InwardTransactionId;

        var requestedQuantity = request.QuantityBoxes ?? putaway.QuantityBoxes;
        var otherLineQuantity = putaway.InwardTransactionLine.Putaways
            .Where(p => p.Id != putaway.Id)
            .Sum(p => p.QuantityBoxes);
        if (otherLineQuantity + requestedQuantity > putaway.InwardTransactionLine.RequestedQuantityBoxes)
        {
            var availableForThisReservation = Math.Max(0,
                putaway.InwardTransactionLine.RequestedQuantityBoxes - otherLineQuantity);
            throw new InvalidOperationException(
                $"Quantity cannot exceed {availableForThisReservation:0.###} box(es) because the line requested " +
                $"{putaway.InwardTransactionLine.RequestedQuantityBoxes:0.###} box(es) in total.");
        }

        var locationChanged = putaway.PalletPositionId != request.NewPalletPositionId;
        var quantityChanged = putaway.QuantityBoxes != requestedQuantity;
        if (!locationChanged && !quantityChanged)
        {
            return await LoadResultAsync(transactionId, cancellationToken);
        }

        var newPosition = await db.PalletPositions
            .FirstOrDefaultAsync(p => p.Id == request.NewPalletPositionId, cancellationToken)
            ?? throw new NotFoundException(nameof(PalletPosition), request.NewPalletPositionId);

        if (!newPosition.IsActive)
        {
            throw new InvalidOperationException($"Location {newPosition.LocationCode} is inactive and cannot receive put-away.");
        }

        if (newPosition.LocationSubtypeId != putaway.PalletPosition.LocationSubtypeId)
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} has a different location subtype and cannot receive this put-away.");
        }

        var material = await db.Materials
            .AsNoTracking()
            .FirstAsync(m => m.Id == putaway.InwardTransactionLine.MaterialId, cancellationToken);

        if (material.RequirePreferredZone && material.PreferredZoneTypeId.HasValue && newPosition.ZoneTypeId != material.PreferredZoneTypeId.Value)
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} is outside this SKU's required zone and cannot receive this put-away.");
        }

        // Every OTHER unconfirmed reservation plus confirmed stock counts as occupied; this
        // putaway's own reservation is excluded since it is the one being moved off this line.
        var otherReservations = await db.InwardPutaways
            .AsNoTracking()
            .Where(p => !p.IsConfirmed && p.Id != putaway.Id && p.PalletPositionId == newPosition.Id)
            .Select(p => new { p.QuantityBoxes, p.InwardTransactionLine.MaterialId, p.InwardTransactionLine.MfgMonth })
            .ToListAsync(cancellationToken);
        var confirmedBatches = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.PalletPositionId == newPosition.Id && b.QuantityBoxes > 0)
            .Select(b => new { b.QuantityBoxes, b.MaterialId, b.MfgMonth })
            .ToListAsync(cancellationToken);

        // Same rule the allocation engine's Tier 2 consolidation uses: a position may only ever
        // hold one material at a time (StockBatch has no DB constraint preventing this, so it must
        // be enforced here too, not just when the engine itself picks a location).
        if (otherReservations.Any(r => r.MaterialId != material.Id) || confirmedBatches.Any(b => b.MaterialId != material.Id))
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} already holds a different material and cannot receive this put-away.");
        }

        var targetWarehouseId = await db.Racks
            .IgnoreQueryFilters()
            .Where(r => r.Id == newPosition.RackId)
            .Select(r => r.WarehouseId)
            .SingleAsync(cancellationToken);
        var allowPkmMixing = await db.Warehouses
            .AsNoTracking()
            .Where(w => w.Id == targetWarehouseId)
            .Select(w => (bool?)w.AllowPkmMixing)
            .SingleOrDefaultAsync(cancellationToken) ?? true;
        if (!allowPkmMixing
            && (otherReservations.Any(r => r.MfgMonth != putaway.InwardTransactionLine.MfgMonth)
                || confirmedBatches.Any(b => b.MfgMonth != putaway.InwardTransactionLine.MfgMonth)))
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} already holds a different PKM and this warehouse does not allow PKM mixing.");
        }

        var reservedByOthers = otherReservations.Sum(r => r.QuantityBoxes);
        var confirmedStock = confirmedBatches.Sum(b => b.QuantityBoxes);

        var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(newPosition, material);
        var free = effectiveCapacity - reservedByOthers - confirmedStock;

        if (free < requestedQuantity)
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} does not have enough free capacity ({free:0.###} of {requestedQuantity:0.###} box(es) needed).");
        }

        putaway.PalletPositionId = newPosition.Id;
        putaway.QuantityBoxes = requestedQuantity;
        putaway.AllocationReason = locationChanged && quantityChanged
            ? "Location and quantity manually changed by operator"
            : locationChanged
                ? "Location manually reassigned by operator"
                : "Quantity manually changed by operator";
        putaway.OverrideReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        putaway.RowVersion++;

        if (quantityChanged)
        {
            var allocated = otherLineQuantity + requestedQuantity;
            putaway.InwardTransactionLine.AllocatedQuantityBoxes = allocated;
            putaway.InwardTransactionLine.Status = allocated >= putaway.InwardTransactionLine.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : allocated > 0
                    ? AllocationStatus.Partial
                    : AllocationStatus.Failed;
            putaway.InwardTransactionLine.Remarks = allocated >= putaway.InwardTransactionLine.RequestedQuantityBoxes
                ? null
                : $"{putaway.InwardTransactionLine.RequestedQuantityBoxes - allocated:0.###} box(es) remain unallocated after the put-away quantity change.";
        }
        await db.SaveChangesAsync(cancellationToken);

        return await LoadResultAsync(transactionId, cancellationToken);
    }

    private async Task<InwardResultDto> LoadResultAsync(int transactionId, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstAsync(t => t.Id == transactionId, cancellationToken);

        return InwardResultMapper.ToResult(transaction);
    }
}
