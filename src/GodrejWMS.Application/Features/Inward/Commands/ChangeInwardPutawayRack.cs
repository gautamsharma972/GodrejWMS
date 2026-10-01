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
/// Moves a selected set of pending put-away reservations to valid positions in one rack. The
/// complete plan is validated before any tracked entity is changed, and one SaveChanges call
/// makes the reassignment atomic.
/// </summary>
public sealed record ChangeInwardPutawayRackCommand(
    IReadOnlyCollection<int> PutawayIds,
    string RackCode,
    string? Reason = null) : IRequest<InwardResultDto>;

public sealed class ChangeInwardPutawayRackValidator : AbstractValidator<ChangeInwardPutawayRackCommand>
{
    public ChangeInwardPutawayRackValidator()
    {
        RuleFor(x => x.PutawayIds).NotEmpty();
        RuleForEach(x => x.PutawayIds).GreaterThan(0);
        RuleFor(x => x.RackCode).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("A reason is required when overriding recommended put-away locations.")
            .MaximumLength(250);
    }
}

public sealed class ChangeInwardPutawayRackHandler(IApplicationDbContext db)
    : IRequestHandler<ChangeInwardPutawayRackCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(ChangeInwardPutawayRackCommand request, CancellationToken cancellationToken)
    {
        var selectedIds = request.PutawayIds.Distinct().ToArray();
        var putaways = await db.InwardPutaways
            .Include(p => p.InwardTransactionLine)
                .ThenInclude(l => l.Material)
            .Include(p => p.PalletPosition)
            .Where(p => selectedIds.Contains(p.Id))
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        if (putaways.Count != selectedIds.Length)
        {
            var foundIds = putaways.Select(p => p.Id).ToHashSet();
            throw new NotFoundException(nameof(InwardPutaway), selectedIds.First(id => !foundIds.Contains(id)));
        }

        if (putaways.Any(p => p.IsConfirmed))
        {
            throw new InvalidOperationException("Confirmed put-away reservations cannot be moved.");
        }

        var transactionIds = putaways
            .Select(p => p.InwardTransactionLine.InwardTransactionId)
            .Distinct()
            .ToArray();
        if (transactionIds.Length != 1)
        {
            throw new InvalidOperationException("Selected put-away reservations must belong to the same inward transaction.");
        }

        var rackCode = request.RackCode.Trim();
        var rack = await db.Racks
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IsActive && r.Code == rackCode, cancellationToken)
            ?? throw new InvalidOperationException($"Rack {rackCode} is inactive or was not found.");

        var positions = await db.PalletPositions
            .Include(p => p.StockBatches)
            .Where(p => p.RackId == rack.Id && p.IsActive)
            .OrderBy(p => p.DistancePriority)
            .ThenBy(p => p.LocationCode)
            .ToListAsync(cancellationToken);

        var otherReservations = await db.InwardPutaways
            .AsNoTracking()
            .Where(p => !p.IsConfirmed && !selectedIds.Contains(p.Id) && p.PalletPosition.RackId == rack.Id)
            .Select(p => new
            {
                p.PalletPositionId,
                p.QuantityBoxes,
                p.InwardTransactionLine.MaterialId
            })
            .ToListAsync(cancellationToken);

        var occupied = positions.ToDictionary(
            p => p.Id,
            p => p.StockBatches.Where(b => b.QuantityBoxes > 0).Sum(b => b.QuantityBoxes)
                + otherReservations.Where(r => r.PalletPositionId == p.Id).Sum(r => r.QuantityBoxes));
        var materialIds = positions.ToDictionary(
            p => p.Id,
            p => p.StockBatches
                .Where(b => b.QuantityBoxes > 0)
                .Select(b => b.MaterialId)
                .Concat(otherReservations.Where(r => r.PalletPositionId == p.Id).Select(r => r.MaterialId))
                .ToHashSet());

        var plan = new Dictionary<int, PalletPosition>();
        foreach (var putaway in putaways
                     .OrderBy(p => p.InwardTransactionLine.MaterialId)
                     .ThenByDescending(p => p.QuantityBoxes)
                     .ThenBy(p => p.Id))
        {
            var material = putaway.InwardTransactionLine.Material;
            var candidate = positions
                .Where(p => p.LocationSubtypeId == putaway.PalletPosition.LocationSubtypeId)
                .Where(p => !material.RequirePreferredZone || !material.PreferredZoneTypeId.HasValue || p.ZoneTypeId == material.PreferredZoneTypeId.Value)
                .Where(p => materialIds[p.Id].Count == 0 || materialIds[p.Id].SetEquals([material.Id]))
                .Where(p => PalletCapacityCalculator.EffectiveCapacityBoxes(p, material) - occupied[p.Id] >= putaway.QuantityBoxes)
                .OrderByDescending(p => materialIds[p.Id].Contains(material.Id))
                .ThenBy(p => p.DistancePriority)
                .ThenBy(p => p.LocationCode)
                .FirstOrDefault();

            if (candidate is null)
            {
                throw new InvalidOperationException(
                    $"Rack {rack.Code} does not have enough compatible capacity to move all {putaways.Count} selected reservation(s). No changes were made.");
            }

            plan[putaway.Id] = candidate;
            occupied[candidate.Id] += putaway.QuantityBoxes;
            materialIds[candidate.Id].Add(material.Id);
        }

        var touchedPositions = new HashSet<int>();
        foreach (var putaway in putaways)
        {
            var newPosition = plan[putaway.Id];
            touchedPositions.Add(putaway.PalletPositionId);
            touchedPositions.Add(newPosition.Id);
            putaway.PalletPositionId = newPosition.Id;
            putaway.AllocationReason = "Rack manually reassigned by operator";
            putaway.OverrideReason = request.Reason!.Trim();
            putaway.RowVersion++;
        }

        foreach (var position in await db.PalletPositions
                     .Where(p => touchedPositions.Contains(p.Id))
                     .ToListAsync(cancellationToken))
        {
            position.RowVersion++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await LoadResultAsync(transactionIds[0], cancellationToken);
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
