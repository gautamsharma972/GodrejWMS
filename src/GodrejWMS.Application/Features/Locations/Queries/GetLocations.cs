using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Locations.Queries;

public sealed record GetLocationsQuery(
    string? Search = null,
    int? LocationTypeId = null,
    int? LocationSubtypeId = null,
    int? ZoneTypeId = null,
    bool? IsActive = null,
    string SortBy = "location",
    bool SortDescending = false,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PaginatedList<PalletPositionDto>>;

public sealed class GetLocationsHandler(IApplicationDbContext db)
    : IRequestHandler<GetLocationsQuery, PaginatedList<PalletPositionDto>>
{
    public async Task<PaginatedList<PalletPositionDto>> Handle(GetLocationsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PalletPositions
            .AsNoTracking()
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
                RackCode = p.Rack.Code,
                OccupiedBoxes = p.StockBatches.Sum(b => (decimal?)b.QuantityBoxes) ?? 0m,
                OccupyingPalletCapacityBoxes = p.StockBatches
                    .Where(b => b.QuantityBoxes > 0)
                    .Select(b => (int?)b.Material.PalletCapacityBoxes)
                    .FirstOrDefault()
            });

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p =>
                p.LocationCode.Contains(term) ||
                (p.FlatLabel != null && p.FlatLabel.Contains(term)) ||
                p.RackCode.Contains(term));
        }

        if (request.LocationTypeId.HasValue)
        {
            query = query.Where(p => p.LocationTypeId == request.LocationTypeId.Value);
        }

        if (request.LocationSubtypeId.HasValue)
        {
            query = query.Where(p => p.LocationSubtypeId == request.LocationSubtypeId.Value);
        }

        if (request.ZoneTypeId.HasValue)
        {
            query = query.Where(p => p.ZoneTypeId == request.ZoneTypeId.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == request.IsActive.Value);
        }

        query = (request.SortBy, request.SortDescending) switch
        {
            ("rack", false) => query.OrderBy(p => p.RackCode).ThenBy(p => p.Column).ThenBy(p => p.Level),
            ("rack", true) => query.OrderByDescending(p => p.RackCode).ThenBy(p => p.Column).ThenBy(p => p.Level),
            ("level", false) => query.OrderBy(p => p.Level).ThenBy(p => p.LocationCode),
            ("level", true) => query.OrderByDescending(p => p.Level).ThenBy(p => p.LocationCode),
            ("column", false) => query.OrderBy(p => p.Column).ThenBy(p => p.LocationCode),
            ("column", true) => query.OrderByDescending(p => p.Column).ThenBy(p => p.LocationCode),
            ("type", false) => query.OrderBy(p => p.LocationTypeName).ThenBy(p => p.LocationCode),
            ("type", true) => query.OrderByDescending(p => p.LocationTypeName).ThenBy(p => p.LocationCode),
            ("subtype", false) => query.OrderBy(p => p.LocationSubtypeName).ThenBy(p => p.LocationCode),
            ("subtype", true) => query.OrderByDescending(p => p.LocationSubtypeName).ThenBy(p => p.LocationCode),
            ("zone", false) => query.OrderBy(p => p.ZoneTypeName).ThenBy(p => p.LocationCode),
            ("zone", true) => query.OrderByDescending(p => p.ZoneTypeName).ThenBy(p => p.LocationCode),
            ("distance", false) => query.OrderBy(p => p.DistancePriority).ThenBy(p => p.LocationCode),
            ("distance", true) => query.OrderByDescending(p => p.DistancePriority).ThenBy(p => p.LocationCode),
            ("occupancy", false) => query.OrderBy(p => p.OccupiedBoxes).ThenBy(p => p.LocationCode),
            ("occupancy", true) => query.OrderByDescending(p => p.OccupiedBoxes).ThenBy(p => p.LocationCode),
            ("active", false) => query.OrderBy(p => p.IsActive).ThenBy(p => p.LocationCode),
            ("active", true) => query.OrderByDescending(p => p.IsActive).ThenBy(p => p.LocationCode),
            (_, true) => query.OrderByDescending(p => p.LocationCode),
            _ => query.OrderBy(p => p.LocationCode)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var page = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var rows = page
            .Select(p =>
            {
                var capacityBoxes = p.OccupyingPalletCapacityBoxes is { } palletCapacity
                    ? (int?)PalletCapacityCalculator.EffectiveCapacityBoxes(p.MaxPallets, palletCapacity)
                    : null;

                return new PalletPositionDto(
                    p.Id,
                    p.RackId,
                    p.LocationCode,
                    p.FlatLabel,
                    p.Column,
                    p.Level,
                    p.LocationTypeId,
                    p.LocationTypeCode,
                    p.LocationTypeName,
                    p.LocationSubtypeId,
                    p.LocationSubtypeCode,
                    p.LocationSubtypeName,
                    p.ZoneTypeId,
                    p.ZoneTypeCode,
                    p.ZoneTypeName,
                    p.DistancePriority,
                    p.MaxPallets,
                    capacityBoxes,
                    p.OccupiedBoxes,
                    capacityBoxes is { } cap ? cap - p.OccupiedBoxes : null,
                    capacityBoxes is { } capForFull && p.OccupiedBoxes >= capForFull,
                    p.IsActive,
                    Array.Empty<PalletPositionStockDto>());
            })
            .ToList();

        return new PaginatedList<PalletPositionDto>(rows, totalCount, request.PageNumber, request.PageSize);
    }
}
