using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Queries;

public sealed record GetPalletPositionByCodeQuery(string LocationCode) : IRequest<PalletPositionDto>;

public sealed class GetPalletPositionByCodeHandler(IApplicationDbContext db)
    : IRequestHandler<GetPalletPositionByCodeQuery, PalletPositionDto>
{
    public async Task<PalletPositionDto> Handle(GetPalletPositionByCodeQuery request, CancellationToken cancellationToken)
    {
        var locationCode = request.LocationCode.Trim();
        var position = await db.PalletPositions
            .AsNoTracking()
            .Where(p => p.LocationCode == locationCode)
            .Select(p => new
            {
                p.Id,
                p.RackId,
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
                // A position only ever holds one material at a time, so the first occupied
                // batch's material (if any) defines this location's effective capacity.
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
                        b.MaterialId,
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
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PalletPosition), locationCode);

        var capacityBoxes = position.OccupyingPalletCapacityBoxes is { } palletCapacity
            ? (int?)PalletCapacityCalculator.EffectiveCapacityBoxes(position.MaxPallets, palletCapacity)
            : null;

        return new PalletPositionDto(
            position.Id,
            position.RackId,
            position.LocationCode,
            position.FlatLabel,
            position.Column,
            position.Level,
            position.LocationTypeId,
            position.LocationTypeCode,
            position.LocationTypeName,
            position.LocationSubtypeId,
            position.LocationSubtypeCode,
            position.LocationSubtypeName,
            position.ZoneTypeId,
            position.ZoneTypeCode,
            position.ZoneTypeName,
            position.DistancePriority,
            position.MaxPallets,
            capacityBoxes,
            position.OccupiedBoxes,
            capacityBoxes is { } cap ? Math.Max(0, cap - position.OccupiedBoxes) : null,
            capacityBoxes is { } capForFull && position.OccupiedBoxes >= capForFull,
            position.IsActive,
            position.Stock.Select(s => new PalletPositionStockDto(
                s.MaterialNumber,
                s.MaterialDescription,
                s.DesignType,
                MfgMonthParser.Format(s.MfgMonth),
                s.StockSubtypeCode,
                s.StockSubtypeName,
                s.QuantityBoxes,
                s.MaterialId,
                s.MfgMonth)).ToList());
    }
}
