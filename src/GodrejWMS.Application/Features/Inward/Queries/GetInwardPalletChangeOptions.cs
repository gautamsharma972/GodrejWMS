using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>
/// Lists active pallet positions the operator can re-route a pending (unconfirmed) put-away
/// reservation to, with enough free capacity to hold it. The position currently holding the
/// reservation is always included and flagged <see cref="InwardPalletChangeOptionDto.IsCurrent"/>.
/// </summary>
public sealed record GetInwardPalletChangeOptionsQuery(int PutawayId) : IRequest<IReadOnlyList<InwardPalletChangeOptionDto>>;

public sealed class GetInwardPalletChangeOptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardPalletChangeOptionsQuery, IReadOnlyList<InwardPalletChangeOptionDto>>
{
    public async Task<IReadOnlyList<InwardPalletChangeOptionDto>> Handle(
        GetInwardPalletChangeOptionsQuery request, CancellationToken cancellationToken)
    {
        var putaway = await db.InwardPutaways
            .AsNoTracking()
            .Include(p => p.InwardTransactionLine)
            .Include(p => p.PalletPosition)
            .FirstOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);

        if (putaway.IsConfirmed)
        {
            throw new InvalidOperationException("This put-away has already been confirmed and can no longer be changed.");
        }

        var material = await db.Materials
            .AsNoTracking()
            .FirstAsync(m => m.Id == putaway.InwardTransactionLine.MaterialId, cancellationToken);

        // Every OTHER unconfirmed reservation counts as occupied; this putaway's own current
        // reservation is excluded so a location can show its true free capacity once this
        // quantity is moved off it.
        var reservedByPosition = await db.InwardPutaways
            .AsNoTracking()
            .Where(p => !p.IsConfirmed && p.Id != putaway.Id)
            .GroupBy(p => p.PalletPositionId)
            .Select(g => new { PalletPositionId = g.Key, QuantityBoxes = g.Sum(p => p.QuantityBoxes) })
            .ToDictionaryAsync(g => g.PalletPositionId, g => g.QuantityBoxes, cancellationToken);

        // Same rule the allocation engine's Tier 2 consolidation and the manual-reroute command
        // enforce: a position may only ever hold one material at a time, so a location already
        // committed to a different SKU (by confirmed stock or another pending reservation) must
        // not be offered as a candidate here.
        var blockedByOtherMaterial = (await db.StockBatches
            .AsNoTracking()
            .Where(b => b.QuantityBoxes > 0 && b.MaterialId != material.Id)
            .Select(b => b.PalletPositionId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .Concat(await db.InwardPutaways
                .AsNoTracking()
                .Where(p => !p.IsConfirmed && p.Id != putaway.Id && p.InwardTransactionLine.MaterialId != material.Id)
                .Select(p => p.PalletPositionId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var positions = await db.PalletPositions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.LocationCode,
                p.LocationSubtypeId,
                LocationSubtypeCode = p.LocationSubtype.Code,
                LocationSubtypeName = p.LocationSubtype.DisplayName,
                p.ZoneTypeId,
                ZoneTypeCode = p.ZoneType.Code,
                ZoneTypeName = p.ZoneType.DisplayName,
                p.DistancePriority,
                p.MaxPallets,
                OccupiedBoxes = p.StockBatches.Sum(b => (decimal?)b.QuantityBoxes) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var options = positions
            .Select(p =>
            {
                var reserved = reservedByPosition.GetValueOrDefault(p.Id);
                var occupied = p.OccupiedBoxes + reserved;
                var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(p.MaxPallets, material.PalletCapacityBoxes);
                var free = Math.Max(0, effectiveCapacity - occupied);
                var isCurrent = p.Id == putaway.PalletPositionId;

                return new InwardPalletChangeOptionDto(
                    p.Id, p.LocationCode, p.LocationSubtypeId, p.LocationSubtypeCode, p.LocationSubtypeName,
                    p.ZoneTypeId, p.ZoneTypeCode, p.ZoneTypeName, p.DistancePriority,
                    (int)effectiveCapacity, occupied, free, isCurrent);
            })
            .Where(o => o.IsCurrent || (o.LocationSubtypeId == putaway.PalletPosition.LocationSubtypeId && o.FreeBoxes >= putaway.QuantityBoxes && !blockedByOtherMaterial.Contains(o.Id)
                && (!material.RequirePreferredZone || !material.PreferredZoneTypeId.HasValue || o.ZoneTypeId == material.PreferredZoneTypeId.Value)))
            .OrderByDescending(o => o.IsCurrent)
            .ThenBy(o => o.DistancePriority)
            .ThenBy(o => o.LocationCode)
            .ToList();

        return options;
    }
}
