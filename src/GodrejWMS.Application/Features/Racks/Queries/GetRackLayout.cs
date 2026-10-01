using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Queries;

/// <summary>Returns a rack's full pallet-position grid with live occupancy, for the rack layout visualization.</summary>
public sealed record GetRackLayoutQuery(int RackId) : IRequest<RackLayoutDto>;

public sealed class GetRackLayoutHandler(IApplicationDbContext db) : IRequestHandler<GetRackLayoutQuery, RackLayoutDto>
{
    public async Task<RackLayoutDto> Handle(GetRackLayoutQuery request, CancellationToken cancellationToken)
    {
        var rack = await db.Racks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RackId, cancellationToken)
            ?? throw new NotFoundException(nameof(Rack), request.RackId);

        var positions = await db.PalletPositions
            .AsNoTracking()
            .Where(p => p.RackId == request.RackId)
            .Select(p => new
            {
                p.Id,
                p.LocationCode,
                p.FlatLabel,
                p.Column,
                p.Level,
                p.LocationTypeId,
                LocationTypeCode = p.LocationType.Code,
                LocationTypeName = p.LocationType.DisplayName,
                p.LocationSubtypeId,
                LocationSubtypeCode = p.LocationSubtype.Code,
                LocationSubtypeName = p.LocationSubtype.DisplayName,
                p.ZoneTypeId,
                ZoneTypeCode = p.ZoneType.Code,
                ZoneTypeName = p.ZoneType.DisplayName,
                p.DistancePriority,
                p.MaxPallets,
                p.IsActive,
                OccupiedBoxes = p.StockBatches.Sum(b => (decimal?)b.QuantityBoxes) ?? 0m,
                OccupyingPalletCapacityBoxes = p.StockBatches
                    .Where(b => b.QuantityBoxes > 0)
                    .Select(b => (int?)b.Material.PalletCapacityBoxes)
                    .FirstOrDefault(),
                Stock = p.StockBatches
                    .Where(b => b.QuantityBoxes > 0)
                    .OrderBy(b => b.Material.MaterialNumber)
                    .ThenBy(b => b.MfgMonth)
                    .Select(b => new
                    {
                        b.Material.MaterialNumber,
                        MaterialDescription = b.Material.Description,
                        b.Material.DesignType,
                        b.MfgMonth,
                        StockSubtypeCode = b.StockSubtype.Code,
                        StockSubtypeName = b.StockSubtype.DisplayName,
                        b.QuantityBoxes
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var dtoPositions = positions
            .OrderBy(p => p.Level).ThenBy(p => p.Column)
            .Select(p =>
            {
                var capacityBoxes = p.OccupyingPalletCapacityBoxes is { } palletCapacity
                    ? (int?)PalletCapacityCalculator.EffectiveCapacityBoxes(p.MaxPallets, palletCapacity)
                    : null;

                return new PalletPositionDto(
                    p.Id, rack.Id, p.LocationCode, p.FlatLabel, p.Column, p.Level,
                    p.LocationTypeId, p.LocationTypeCode, p.LocationTypeName, p.LocationSubtypeId, p.LocationSubtypeCode, p.LocationSubtypeName, p.ZoneTypeId, p.ZoneTypeCode, p.ZoneTypeName, p.DistancePriority, p.MaxPallets, capacityBoxes,
                    p.OccupiedBoxes, capacityBoxes is { } cap ? Math.Max(0, cap - p.OccupiedBoxes) : null, capacityBoxes is { } capForFull && p.OccupiedBoxes >= capForFull, p.IsActive,
                    p.Stock.Select(s => new PalletPositionStockDto(
                        s.MaterialNumber,
                        s.MaterialDescription,
                        s.DesignType,
                        MfgMonthParser.Format(s.MfgMonth),
                        s.StockSubtypeCode,
                        s.StockSubtypeName,
                        s.QuantityBoxes)).ToList());
            })
            .ToList();

        return new RackLayoutDto(rack.Id, rack.Code, rack.Columns, rack.Levels, dtoPositions);
    }
}
