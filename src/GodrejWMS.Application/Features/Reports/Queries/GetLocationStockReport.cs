using GodrejWMS.Domain.Entities;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Reports.Dtos;
using GodrejWMS.Application.Features.StockMaster;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

public sealed record LocationStockReportFilter(
    int? WarehouseId = null, string? Search = null,
    /// <summary>"IN-STOCK", "GIT-IN" or "GIT-OUT": only rows holding boxes of that status (see StockFlowStatus). Null = all.</summary>
    string? Status = null);

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

        // A free virtual location is not a warehouse location worth listing; one in use (holding overflow stock) is.
        var positions = db.PalletPositions.AsNoTracking().Where(p => p.IsActive && (!p.IsVirtual || p.VirtualStatus == VirtualLocationStatus.InUse));
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
                b.MaterialId,
                b.StockSubtypeId,
                b.Material.MaterialNumber,
                b.Material.Description,
                AopDescription = b.Material.AopDescription != null ? b.Material.AopDescription.Code : null,
                b.Material.DesignType,
                b.Material.PalletCapacityBoxes,
                b.MfgMonth,
                b.QuantityBoxes
            })
            .ToListAsync(cancellationToken);

        // Flow status (display only): unconfirmed put-away boxes of a cell are GIT-IN; boxes of unconfirmed picks (already deducted) are GIT-OUT.
        var pending = await StockFlowStatus.PendingPutawayByCellAsync(db, filter.WarehouseId, cancellationToken);
        var picks = await StockFlowStatus.OpenPicksAsync(db, filter.WarehouseId, cancellationToken);
        var gitOutByCell = picks.GroupBy(p => (p.PalletPositionId, p.MaterialId, p.MfgMonth)).ToDictionary(g => g.Key, g => g.Sum(p => p.QuantityBoxes));
        var pickMaterialIds = picks.Select(p => p.MaterialId).Distinct().ToList();
        var pickMaterials = pickMaterialIds.Count == 0
            ? new Dictionary<int, (long MaterialNumber, string Description, string? Aop, string DesignType, int PalletCapacityBoxes, bool IsActive)>()
            : (await db.Materials.AsNoTracking().Where(m => pickMaterialIds.Contains(m.Id))
                .Select(m => new
                {
                    m.Id, m.MaterialNumber, m.Description, Aop = m.AopDescription != null ? m.AopDescription.Code : null,
                    m.DesignType, m.PalletCapacityBoxes, m.IsActive
                }).ToListAsync(cancellationToken))
              .ToDictionary(m => m.Id, m => (m.MaterialNumber, m.Description, m.Aop, m.DesignType, m.PalletCapacityBoxes, m.IsActive));

        var batchesByLocation = batches.ToLookup(b => b.PalletPositionId);
        var outByLocation = gitOutByCell.ToLookup(kv => kv.Key.PalletPositionId);
        var rows = new List<LocationStockReportRowDto>();
        foreach (var location in locations)
        {
            var held = batchesByLocation[location.Id].OrderBy(b => b.MaterialNumber).ThenBy(b => b.MfgMonth).ToList();
            var outHere = outByLocation[location.Id].ToList();
            if (held.Count == 0 && outHere.Count == 0)
            {
                rows.Add(new LocationStockReportRowDto(location.LocationCode, null, null, null, null, null, 0, null));
                continue;
            }

            // A location holding several materials is only as roomy as its smallest Pallet Size.
            var capacitySizes = held.Select(b => b.PalletCapacityBoxes).ToList();
            foreach (var kv in outHere)
            {
                if (pickMaterials.TryGetValue(kv.Key.MaterialId, out var pm) && pm.IsActive) capacitySizes.Add(pm.PalletCapacityBoxes);
            }

            var capacity = (int)PalletCapacityCalculator.EffectiveCapacityBoxes(location.MaxPallets, capacitySizes);
            var rowsBefore = rows.Count;
            var covered = new HashSet<(int MaterialId, int Mfg)>();
            foreach (var b in held)
            {
                var pendingBoxes = b.StockSubtypeId == LocationSubtypeIds.Good ? pending.GetValueOrDefault((b.MaterialId, b.MfgMonth, b.PalletPositionId)) : 0m;
                var (inStock, gitIn) = StockFlowStatus.Split(b.QuantityBoxes, pendingBoxes);
                covered.Add((b.MaterialId, b.MfgMonth));
                rows.Add(new LocationStockReportRowDto(
                    location.LocationCode, b.MaterialNumber, b.Description, b.AopDescription, b.DesignType, b.MfgMonth, b.QuantityBoxes, capacity,
                    inStock, gitIn, gitOutByCell.GetValueOrDefault((location.Id, b.MaterialId, b.MfgMonth))));
            }

            // Boxes of unconfirmed picks whose material + PKM no longer has a stock row here: the location is not empty on the rack.
            foreach (var kv in outHere.Where(kv => !covered.Contains((kv.Key.MaterialId, kv.Key.MfgMonth))).OrderBy(kv => kv.Key.MaterialId).ThenBy(kv => kv.Key.MfgMonth))
            {
                if (!pickMaterials.TryGetValue(kv.Key.MaterialId, out var pm) || !pm.IsActive) continue;
                rows.Add(new LocationStockReportRowDto(
                    location.LocationCode, pm.MaterialNumber, pm.Description, pm.Aop, pm.DesignType, kv.Key.MfgMonth, 0, capacity, 0, 0, kv.Value));
            }

            if (rows.Count == rowsBefore)
            {
                rows.Add(new LocationStockReportRowDto(location.LocationCode, null, null, null, null, null, 0, null));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            rows = rows.Where(r =>
                r.LocationCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.MaterialCode?.ToString().Contains(term) ?? false) ||
                (r.MaterialDescription?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            rows = filter.Status.Trim().ToUpperInvariant() switch
            {
                StockFlowStatus.InStock => rows.Where(r => r.InStockQty > 0).ToList(),
                StockFlowStatus.GitIn => rows.Where(r => r.GitInQty > 0).ToList(),
                StockFlowStatus.GitOut => rows.Where(r => r.GitOutQty > 0).ToList(),
                _ => rows
            };
        }

        return rows.OrderBy(r => r.LocationCode, StringComparer.Ordinal).ThenBy(r => r.Pkm).ToList();
    }
}
