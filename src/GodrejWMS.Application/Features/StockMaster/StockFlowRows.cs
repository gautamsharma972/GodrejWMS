using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using GodrejWMS.Application.Features.StockMaster.Queries;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster;

/// <summary>One row of the Current Inventory grid with its flow status. Built from the live stock batches plus the unconfirmed put-aways and picks
/// (<see cref="StockFlowStatus"/>); read-only, never feeds back into stock.</summary>
public sealed record StockFlowRow(
    int StockBatchId, int MaterialId, int PalletPositionId, long MaterialNumber, string Description, string DesignType,
    int MovementTypeId, string MovementTypeCode, string MovementTypeName, int SeasonId, string SeasonCode, string SeasonName, bool IsActive,
    decimal QuantityBoxes, int MfgMonth, int StockSubtypeId, string StockSubtypeName, string LocationCode, string RackCode, int WarehouseId,
    bool IsVirtualLocation, string Status);

public static class StockFlowRows
{
    public static async Task<List<StockFlowRow>> LoadAsync(IApplicationDbContext db, GetStockMasterQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes > 0)
            .Select(b => new
            {
                StockBatchId = b.Id, b.MaterialId, b.PalletPositionId,
                b.Material.MaterialNumber, b.Material.Description, b.Material.DesignType, b.Material.MovementTypeId,
                MovementTypeCode = b.Material.MovementType.Code, MovementTypeName = b.Material.MovementType.DisplayName,
                b.Material.SeasonId, SeasonCode = b.Material.Season.Code, SeasonName = b.Material.Season.DisplayName,
                b.Material.IsActive, b.QuantityBoxes, b.MfgMonth, b.StockSubtypeId, StockSubtypeName = b.StockSubtype.DisplayName,
                b.PalletPosition.LocationCode, RackCode = b.PalletPosition.Rack.Code, b.PalletPosition.Rack.WarehouseId,
                IsVirtualLocation = b.PalletPosition.IsVirtual
            });

        // Same filters as the plain grid.
        if (request.StockSubtypeId.HasValue) query = query.Where(b => b.StockSubtypeId == request.StockSubtypeId.Value);
        if (request.IsVirtual.HasValue) query = query.Where(b => b.IsVirtualLocation == request.IsVirtual.Value);
        if (request.WarehouseId.HasValue) query = query.Where(b => b.WarehouseId == request.WarehouseId.Value);
        if (!string.IsNullOrWhiteSpace(request.MaterialSearch))
        {
            var term = request.MaterialSearch.Trim();
            query = query.Where(b => b.Description.Contains(term) || b.MaterialNumber.ToString().Contains(term) || b.LocationCode.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.DesignType)) query = query.Where(b => b.DesignType == request.DesignType);
        if (request.MovementTypeId.HasValue) query = query.Where(b => b.MovementTypeId == request.MovementTypeId.Value);
        if (request.SeasonId.HasValue) query = query.Where(b => b.SeasonId == request.SeasonId.Value);
        if (request.IsActive.HasValue) query = query.Where(b => b.IsActive == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.RackCode)) query = query.Where(b => b.RackCode == request.RackCode);

        var batches = await query.ToListAsync(cancellationToken);
        var pending = await StockFlowStatus.PendingPutawayByCellAsync(db, request.WarehouseId, cancellationToken);

        var rows = new List<StockFlowRow>();
        foreach (var b in batches)
        {
            StockFlowRow Make(decimal quantity, string status) => new(
                b.StockBatchId, b.MaterialId, b.PalletPositionId, b.MaterialNumber, b.Description, b.DesignType, b.MovementTypeId, b.MovementTypeCode,
                b.MovementTypeName, b.SeasonId, b.SeasonCode, b.SeasonName, b.IsActive, quantity, b.MfgMonth, b.StockSubtypeId, b.StockSubtypeName,
                b.LocationCode, b.RackCode, b.WarehouseId, b.IsVirtualLocation, status);

            // Only Good stock comes from put-aways; damaged stock is always in stock.
            var pendingBoxes = b.StockSubtypeId == LocationSubtypeIds.Good ? pending.GetValueOrDefault((b.MaterialId, b.MfgMonth, b.PalletPositionId)) : 0m;
            var (inStock, gitIn) = StockFlowStatus.Split(b.QuantityBoxes, pendingBoxes);
            if (inStock > 0) rows.Add(Make(inStock, StockFlowStatus.InStock));
            if (gitIn > 0) rows.Add(Make(gitIn, StockFlowStatus.GitIn));
        }

        // GIT-OUT: boxes of unconfirmed picks. They are not in StockBatches (already deducted), so they come from the picks themselves; they are Good stock.
        if (!request.StockSubtypeId.HasValue || request.StockSubtypeId == LocationSubtypeIds.Good)
        {
            var picks = await StockFlowStatus.OpenPicksAsync(db, request.WarehouseId, cancellationToken);
            var cells = picks.GroupBy(p => (p.MaterialId, p.MfgMonth, p.PalletPositionId)).Select(g => (g.Key, Boxes: g.Sum(p => p.QuantityBoxes))).ToList();
            if (cells.Count > 0)
            {
                var materialIds = cells.Select(c => c.Key.MaterialId).Distinct().ToList();
                var positionIds = cells.Select(c => c.Key.PalletPositionId).Distinct().ToList();
                var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
                    .Select(m => new
                    {
                        m.Id, m.MaterialNumber, m.Description, m.DesignType, m.MovementTypeId, MovementTypeCode = m.MovementType.Code,
                        MovementTypeName = m.MovementType.DisplayName, m.SeasonId, SeasonCode = m.Season.Code, SeasonName = m.Season.DisplayName, m.IsActive
                    }).ToDictionaryAsync(m => m.Id, cancellationToken);
                var positions = await db.PalletPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.LocationCode, RackCode = p.Rack.Code, p.Rack.WarehouseId, p.IsVirtual })
                    .ToDictionaryAsync(p => p.Id, cancellationToken);
                var good = await db.LocationSubtypes.AsNoTracking().Where(t => t.Id == LocationSubtypeIds.Good).Select(t => t.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "Good";

                foreach (var (key, boxes) in cells)
                {
                    if (!materials.TryGetValue(key.MaterialId, out var m) || !positions.TryGetValue(key.PalletPositionId, out var p)) continue;
                    if (request.WarehouseId.HasValue && p.WarehouseId != request.WarehouseId.Value) continue;
                    if (request.IsVirtual.HasValue && p.IsVirtual != request.IsVirtual.Value) continue;
                    if (!string.IsNullOrWhiteSpace(request.RackCode) && p.RackCode != request.RackCode) continue;
                    if (!string.IsNullOrWhiteSpace(request.DesignType) && m.DesignType != request.DesignType) continue;
                    if (request.MovementTypeId.HasValue && m.MovementTypeId != request.MovementTypeId.Value) continue;
                    if (request.SeasonId.HasValue && m.SeasonId != request.SeasonId.Value) continue;
                    if (request.IsActive.HasValue && m.IsActive != request.IsActive.Value) continue;
                    if (!string.IsNullOrWhiteSpace(request.MaterialSearch))
                    {
                        var term = request.MaterialSearch.Trim();
                        if (!(m.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
                              || m.MaterialNumber.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)
                              || p.LocationCode.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                    }

                    rows.Add(new StockFlowRow(0, key.MaterialId, key.PalletPositionId, m.MaterialNumber, m.Description, m.DesignType, m.MovementTypeId,
                        m.MovementTypeCode, m.MovementTypeName, m.SeasonId, m.SeasonCode, m.SeasonName, m.IsActive, boxes, key.MfgMonth,
                        LocationSubtypeIds.Good, good, p.LocationCode, p.RackCode, p.WarehouseId, p.IsVirtual, StockFlowStatus.GitOut));
                }
            }
        }

        return rows;
    }

    public static IEnumerable<StockFlowRow> Sort(IEnumerable<StockFlowRow> rows, string sortBy, bool descending)
    {
        IOrderedEnumerable<StockFlowRow> By<TKey>(Func<StockFlowRow, TKey> key) =>
            descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

        var ordered = sortBy switch
        {
            "description" => By(r => r.Description),
            "design" => By(r => r.DesignType),
            "quantity" => By(r => r.QuantityBoxes),
            "mfg" => By(r => r.MfgMonth),
            "location" => By(r => r.LocationCode),
            "velocity" => By(r => r.MovementTypeId),
            "season" => By(r => r.SeasonId),
            "active" => By(r => r.IsActive),
            "status" => By(r => r.Status),
            _ => By(r => r.MaterialNumber)
        };
        return ordered.ThenBy(r => r.MaterialNumber).ThenBy(r => r.MfgMonth).ThenBy(r => r.LocationCode, StringComparer.Ordinal).ThenBy(r => r.Status);
    }

    public static StockMasterRowDto ToDto(StockFlowRow b) => new(
        b.MaterialNumber, b.Description, b.DesignType, b.QuantityBoxes, MfgMonthParser.Format(b.MfgMonth), b.LocationCode,
        b.MovementTypeCode, b.MovementTypeName, b.SeasonCode, b.SeasonName, b.IsActive, b.StockBatchId, b.MaterialId, b.PalletPositionId, b.MfgMonth,
        b.StockSubtypeName, b.IsVirtualLocation, b.StockSubtypeId, b.Status);
}
