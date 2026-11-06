using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Reports.Dtos;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

public sealed record LocationStockReportFilter(int? WarehouseId = null, string? Search = null);

public sealed record GetLocationStockReportQuery(LocationStockReportFilter Filter)
    : IRequest<IReadOnlyList<LocationStockReportRowDto>>;

/// <summary>Every active location, occupied and empty: one row per material + PKM batch held, or a single row with no
/// material for an empty location. Stock of deactivated materials does not occupy a location.</summary>
public sealed class GetLocationStockReportHandler(
    IApplicationDbContext db,
    IWarehouseAccessService warehouseAccess,
    IPermissionService permissions)
    : IRequestHandler<GetLocationStockReportQuery, IReadOnlyList<LocationStockReportRowDto>>
{
    public async Task<IReadOnlyList<LocationStockReportRowDto>> Handle(
        GetLocationStockReportQuery request,
        CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Reports.View, cancellationToken))
            throw new UnauthorizedAccessException("Reports require an authorized warehouse role.");

        var filter = request.Filter;
        if (filter.WarehouseId is int warehouseId &&
            !await warehouseAccess.HasAccessAsync(warehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var positions = db.PalletPositions.AsNoTracking().Where(p => p.IsActive);
        if (filter.WarehouseId.HasValue)
            positions = positions.Where(p => p.Rack.WarehouseId == filter.WarehouseId.Value);

        var locations = await positions
            .Select(p => new { p.Id, p.LocationCode, p.MaxPallets })
            .ToListAsync(cancellationToken);

        var batches = await db.StockBatches.AsNoTracking()
            .Where(b => b.QuantityBoxes > 0 && b.Material.IsActive && b.PalletPosition.IsActive)
            .Where(b => !filter.WarehouseId.HasValue || b.PalletPosition.Rack.WarehouseId == filter.WarehouseId.Value)
            .Select(b => new
            {
                b.PalletPositionId,
                b.Material.MaterialNumber,
                b.Material.Description,
                AopDescription = b.Material.AopDescription != null ? b.Material.AopDescription.Code : null,
                b.Material.DesignType,
                b.Material.PalletCapacityBoxes,
                b.MfgMonth,
                b.QuantityBoxes
            })
            .ToListAsync(cancellationToken);

        var batchesByLocation = batches.ToLookup(b => b.PalletPositionId);
        var rows = new List<LocationStockReportRowDto>();
        foreach (var location in locations)
        {
            var held = batchesByLocation[location.Id].OrderBy(b => b.MaterialNumber).ThenBy(b => b.MfgMonth).ToList();
            if (held.Count == 0)
            {
                rows.Add(new LocationStockReportRowDto(location.LocationCode, null, null, null, null, null, 0, null));
                continue;
            }

            // A location holding several materials is only as roomy as its smallest Pallet Size.
            var capacity = (int)PalletCapacityCalculator.EffectiveCapacityBoxes(location.MaxPallets, held.Select(b => b.PalletCapacityBoxes));
            rows.AddRange(held.Select(b => new LocationStockReportRowDto(
                location.LocationCode,
                b.MaterialNumber,
                b.Description,
                b.AopDescription,
                b.DesignType,
                b.MfgMonth,
                b.QuantityBoxes,
                capacity)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            rows = rows.Where(r =>
                r.LocationCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.MaterialCode?.ToString().Contains(term) ?? false) ||
                (r.MaterialDescription?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        return rows.OrderBy(r => r.LocationCode, StringComparer.Ordinal).ThenBy(r => r.Pkm).ToList();
    }
}
