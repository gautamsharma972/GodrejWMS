using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Re-routes a pending (unconfirmed) put-away reservation to a different pallet position, before
/// the operator confirms it. Confirmed put-aways already have stock booked and can no longer be
/// moved this way. A reason is mandatory since this always overrides the engine's own recommendation.
/// </summary>
public sealed record ChangeInwardPutawayLocationCommand(int PutawayId, int NewPalletPositionId, string? Reason = null) : IRequest<InwardResultDto>;

public sealed class ChangeInwardPutawayLocationValidator : AbstractValidator<ChangeInwardPutawayLocationCommand>
{
    public ChangeInwardPutawayLocationValidator()
    {
        RuleFor(x => x.PutawayId).GreaterThan(0);
        RuleFor(x => x.NewPalletPositionId).GreaterThan(0);
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
            .Include(p => p.PalletPosition)
            .FirstOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);

        if (putaway.IsConfirmed)
        {
            throw new InvalidOperationException("This put-away has already been confirmed and can no longer be changed.");
        }

        var transactionId = putaway.InwardTransactionLine.InwardTransactionId;

        if (putaway.PalletPositionId == request.NewPalletPositionId)
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
            .Select(p => new { p.QuantityBoxes, p.InwardTransactionLine.MaterialId })
            .ToListAsync(cancellationToken);
        var confirmedBatches = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.PalletPositionId == newPosition.Id && b.QuantityBoxes > 0)
            .Select(b => new { b.QuantityBoxes, b.MaterialId })
            .ToListAsync(cancellationToken);

        // Same rule the allocation engine's Tier 2 consolidation uses: a position may only ever
        // hold one material at a time (StockBatch has no DB constraint preventing this, so it must
        // be enforced here too, not just when the engine itself picks a location).
        if (otherReservations.Any(r => r.MaterialId != material.Id) || confirmedBatches.Any(b => b.MaterialId != material.Id))
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} already holds a different material and cannot receive this put-away.");
        }

        var reservedByOthers = otherReservations.Sum(r => r.QuantityBoxes);
        var confirmedStock = confirmedBatches.Sum(b => b.QuantityBoxes);

        var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(newPosition, material);
        var free = effectiveCapacity - reservedByOthers - confirmedStock;

        if (free < putaway.QuantityBoxes)
        {
            throw new InvalidOperationException(
                $"Location {newPosition.LocationCode} does not have enough free capacity ({free:0.###} of {putaway.QuantityBoxes:0.###} box(es) needed).");
        }

        putaway.PalletPositionId = newPosition.Id;
        putaway.AllocationReason = "Location manually reassigned by operator";
        putaway.OverrideReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        putaway.RowVersion++;
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
