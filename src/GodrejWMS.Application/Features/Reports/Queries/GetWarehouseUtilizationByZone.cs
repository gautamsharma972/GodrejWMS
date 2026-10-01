using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inventory Reports - "Warehouse Utilization by Zone/Type" tab: occupancy % broken out
/// by zone and by location type, instead of the Dashboard's single warehouse-wide number. No
/// existing query aggregates this way (confirmed by grep before building it) - all the underlying
/// per-location fields already exist on PalletPosition.</summary>
public sealed record GetWarehouseUtilizationByZoneQuery : IRequest<WarehouseUtilizationDto>;

public sealed class GetWarehouseUtilizationByZoneHandler(IApplicationDbContext db)
    : IRequestHandler<GetWarehouseUtilizationByZoneQuery, WarehouseUtilizationDto>
{
    public async Task<WarehouseUtilizationDto> Handle(GetWarehouseUtilizationByZoneQuery request, CancellationToken cancellationToken)
    {
        var perPosition = await db.PalletPositions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new
            {
                ZoneCode = p.ZoneType.Code,
                ZoneName = p.ZoneType.DisplayName,
                LocationTypeCode = p.LocationType.Code,
                LocationTypeName = p.LocationType.DisplayName,
                // Capacity is driven entirely by whichever material occupies a position; an
                // empty location contributes 0 since there's no material context.
                CapacityBoxes = p.MaxPallets * (p.StockBatches.Where(b => b.QuantityBoxes > 0).Select(b => (int?)b.Material.PalletCapacityBoxes).FirstOrDefault() ?? 0),
                OccupiedBoxes = p.StockBatches.Sum(b => (decimal?)b.QuantityBoxes) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var byZone = perPosition
            .GroupBy(p => new { p.ZoneCode, p.ZoneName })
            .Select(g =>
            {
                var capacity = g.Sum(x => x.CapacityBoxes);
                var occupied = g.Sum(x => x.OccupiedBoxes);
                return new ZoneUtilizationRowDto(
                    g.Key.ZoneCode, g.Key.ZoneName, g.Count(), capacity, occupied,
                    capacity == 0 ? 0 : Math.Round(100m * occupied / capacity, 1));
            })
            .OrderByDescending(r => r.OccupancyPercent)
            .ToList();

        var byLocationType = perPosition
            .GroupBy(p => new { p.LocationTypeCode, p.LocationTypeName })
            .Select(g =>
            {
                var capacity = g.Sum(x => x.CapacityBoxes);
                var occupied = g.Sum(x => x.OccupiedBoxes);
                return new LocationTypeUtilizationRowDto(
                    g.Key.LocationTypeCode, g.Key.LocationTypeName, g.Count(), capacity, occupied,
                    capacity == 0 ? 0 : Math.Round(100m * occupied / capacity, 1));
            })
            .OrderByDescending(r => r.OccupancyPercent)
            .ToList();

        return new WarehouseUtilizationDto(byZone, byLocationType);
    }
}
