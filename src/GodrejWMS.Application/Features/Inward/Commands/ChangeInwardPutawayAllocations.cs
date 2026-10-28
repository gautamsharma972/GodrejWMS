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

public sealed record InwardPutawayDestination(int PalletPositionId, decimal QuantityBoxes);

public sealed record ChangeInwardPutawayAllocationsCommand(
    int PutawayId,
    IReadOnlyList<InwardPutawayDestination> Destinations,
    string Reason) : IRequest<InwardResultDto>;

public sealed class ChangeInwardPutawayAllocationsValidator : AbstractValidator<ChangeInwardPutawayAllocationsCommand>
{
    public ChangeInwardPutawayAllocationsValidator()
    {
        RuleFor(x => x.PutawayId).GreaterThan(0);
        RuleFor(x => x.Destinations).NotEmpty().WithMessage("Select at least one put-away location.");
        RuleForEach(x => x.Destinations).ChildRules(destination =>
        {
            destination.RuleFor(x => x.PalletPositionId).GreaterThan(0);
            destination.RuleFor(x => x.QuantityBoxes).GreaterThan(0)
                .Must(decimal.IsInteger).WithMessage("Quantity must be a whole number.");
        });
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(250);
    }
}

public sealed class ChangeInwardPutawayAllocationsHandler(IApplicationDbContext db)
    : IRequestHandler<ChangeInwardPutawayAllocationsCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(ChangeInwardPutawayAllocationsCommand request, CancellationToken cancellationToken)
    {
        if (request.Destinations.GroupBy(x => x.PalletPositionId).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Each put-away location can only be selected once.");

        var putaway = await db.InwardPutaways
            .Include(p => p.PalletPosition)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.Material)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.Putaways)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.InwardTransaction)
            .SingleOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);

        if (putaway.IsConfirmed)
            throw new InvalidOperationException("This put-away has already been confirmed and can no longer be changed.");
        if (putaway.InwardTransactionLine.InwardTransaction.IsRejected)
            throw new InvalidOperationException("A rejected GRN cannot be changed.");

        var line = putaway.InwardTransactionLine;
        var otherLineQuantity = line.Putaways.Where(p => p.Id != putaway.Id).Sum(p => p.QuantityBoxes);
        var selectedQuantity = request.Destinations.Sum(x => x.QuantityBoxes);
        if (otherLineQuantity + selectedQuantity > line.RequestedQuantityBoxes)
            throw new InvalidOperationException(
                $"Selected locations total {selectedQuantity:0.###} box(es), which would exceed the material's inward quantity of {line.RequestedQuantityBoxes:0.###} box(es).");

        var positionIds = request.Destinations.Select(x => x.PalletPositionId).ToArray();
        var positions = await db.PalletPositions.Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        if (positions.Count != positionIds.Length)
            throw new InvalidOperationException("One or more selected put-away locations no longer exist.");

        var allowPkmMixing = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == line.InwardTransaction.WarehouseId)
            .Select(w => (bool?)w.AllowPkmMixing).SingleOrDefaultAsync(cancellationToken) ?? true;
        var otherReservations = await db.InwardPutaways.AsNoTracking()
            .Where(p => !p.IsConfirmed && p.Id != putaway.Id && positionIds.Contains(p.PalletPositionId))
            .Select(p => new { p.PalletPositionId, p.QuantityBoxes, p.InwardTransactionLine.MaterialId, p.InwardTransactionLine.MfgMonth })
            .ToListAsync(cancellationToken);
        var batches = await db.StockBatches.AsNoTracking()
            .Where(b => positionIds.Contains(b.PalletPositionId) && b.QuantityBoxes > 0)
            .Select(b => new { b.PalletPositionId, b.QuantityBoxes, b.MaterialId, b.MfgMonth })
            .ToListAsync(cancellationToken);

        var occupied = positionIds.Distinct().ToDictionary(id => id,
            id => otherReservations.Where(r => r.PalletPositionId == id).Sum(r => r.QuantityBoxes)
                + batches.Where(b => b.PalletPositionId == id).Sum(b => b.QuantityBoxes));
        foreach (var destination in request.Destinations)
        {
            var position = positions[destination.PalletPositionId];
            if (!position.IsActive || position.LocationSubtypeId != putaway.PalletPosition.LocationSubtypeId)
                throw new InvalidOperationException($"Location {position.LocationCode} is not an active compatible put-away location.");
            if (line.Material.RequirePreferredZone && line.Material.PreferredZoneTypeId.HasValue
                && position.ZoneTypeId != line.Material.PreferredZoneTypeId.Value)
                throw new InvalidOperationException($"Location {position.LocationCode} is outside this SKU's required zone.");
            if (otherReservations.Any(r => r.PalletPositionId == position.Id && r.MaterialId != line.MaterialId)
                || batches.Any(b => b.PalletPositionId == position.Id && b.MaterialId != line.MaterialId))
                throw new InvalidOperationException($"Location {position.LocationCode} already holds a different material.");
            if (!allowPkmMixing && (otherReservations.Any(r => r.PalletPositionId == position.Id && r.MfgMonth != line.MfgMonth)
                || batches.Any(b => b.PalletPositionId == position.Id && b.MfgMonth != line.MfgMonth)))
                throw new InvalidOperationException($"Location {position.LocationCode} already holds a different PKM and this warehouse does not allow PKM mixing.");

            var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position, line.Material);
            var free = capacity - occupied[position.Id];
            if (destination.QuantityBoxes > free)
                throw new InvalidOperationException($"Location {position.LocationCode} has only {Math.Max(0, free):0.###} free box(es).");
            occupied[position.Id] += destination.QuantityBoxes;
        }

        var reason = request.Reason.Trim();
        var first = request.Destinations[0];
        putaway.PalletPositionId = first.PalletPositionId;
        putaway.QuantityBoxes = first.QuantityBoxes;
        putaway.AllocationReason = "Put-away allocation manually changed by operator";
        putaway.OverrideReason = reason;
        putaway.RowVersion++;
        foreach (var destination in request.Destinations.Skip(1))
        {
            line.Putaways.Add(new InwardPutaway
            {
                PalletPositionId = destination.PalletPositionId,
                QuantityBoxes = destination.QuantityBoxes,
                AllocationReason = "Put-away allocation manually split by operator",
                OverrideReason = reason
            });
        }

        var newTotal = otherLineQuantity + selectedQuantity;
        line.AllocatedQuantityBoxes = newTotal;
        line.Status = newTotal == line.RequestedQuantityBoxes ? AllocationStatus.Fulfilled
            : newTotal > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
        line.Remarks = line.Status == AllocationStatus.Fulfilled ? null
            : $"{line.RequestedQuantityBoxes - newTotal:0.###} box(es) remain unallocated after the put-away change.";
        await db.SaveChangesAsync(cancellationToken);

        var transaction = await db.InwardTransactions.AsNoTracking()
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Putaways).ThenInclude(p => p.PalletPosition)
            .SingleAsync(t => t.Id == line.InwardTransactionId, cancellationToken);
        return InwardResultMapper.ToResult(transaction);
    }
}
