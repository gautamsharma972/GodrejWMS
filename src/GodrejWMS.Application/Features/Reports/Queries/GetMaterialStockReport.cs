using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Reports.Dtos;
using GodrejWMS.Application.Features.StockMaster;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

public sealed record MaterialStockReportFilter(
    int? WarehouseId = null,
    string? Search = null,
    int? PkmFrom = null,
    int? PkmTo = null,
    decimal? MinMrp = null,
    decimal? MaxMrp = null,
    decimal? MinBoxes = null,
    decimal? MaxBoxes = null,
    /// <summary>"IN-STOCK", "GIT-IN" or "GIT-OUT": only rows that hold boxes of that status (see StockFlowStatus). Null = all.</summary>
    string? Status = null);

public sealed record GetMaterialStockReportQuery(MaterialStockReportFilter Filter)
    : IRequest<IReadOnlyList<MaterialStockReportRowDto>>;

public sealed class GetMaterialStockReportHandler(
    IApplicationDbContext db,
    IWarehouseAccessService warehouseAccess,
    IPermissionService permissions)
    : IRequestHandler<GetMaterialStockReportQuery, IReadOnlyList<MaterialStockReportRowDto>>
{
    public async Task<IReadOnlyList<MaterialStockReportRowDto>> Handle(
        GetMaterialStockReportQuery request,
        CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Reports.View, cancellationToken))
            throw new UnauthorizedAccessException("Reports require an authorized warehouse role.");

        var filter = request.Filter;
        Validate(filter);

        if (filter.WarehouseId is int warehouseId &&
            !await warehouseAccess.HasAccessAsync(warehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var query = db.StockBatches
            .AsNoTracking()
            .Where(batch => batch.QuantityBoxes > 0);

        if (filter.WarehouseId.HasValue)
            query = query.Where(batch => batch.PalletPosition.Rack.WarehouseId == filter.WarehouseId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(batch =>
                batch.Material.MaterialNumber.ToString().Contains(term) ||
                batch.Material.Description.Contains(term));
        }

        if (filter.PkmFrom.HasValue)
            query = query.Where(batch => batch.MfgMonth >= filter.PkmFrom.Value);
        if (filter.PkmTo.HasValue)
            query = query.Where(batch => batch.MfgMonth <= filter.PkmTo.Value);
        if (filter.MinMrp.HasValue)
            query = query.Where(batch => batch.Material.MrpPrice >= filter.MinMrp.Value);
        if (filter.MaxMrp.HasValue)
            query = query.Where(batch => batch.Material.MrpPrice <= filter.MaxMrp.Value);

        // Materialize the already-filtered batch projection before grouping. The application's
        // MySQL provider cannot translate a record-constructor projection over this query once
        // warehouse access filters and navigation joins have been expanded.
        var batches = await query
            .Select(batch => new
            {
                batch.MaterialId,
                batch.Material.MaterialNumber,
                MaterialDescription = batch.Material.Description,
                batch.MfgMonth,
                batch.Material.MrpPrice,
                batch.QuantityBoxes,
                batch.PalletPositionId,
                batch.StockSubtypeId
            })
            .ToListAsync(cancellationToken);

        // Flow status (display only): the part of each Good cell that is still unconfirmed put-away is GIT-IN; boxes of unconfirmed picks are GIT-OUT.
        var pending = await StockFlowStatus.PendingPutawayByCellAsync(db, filter.WarehouseId, cancellationToken);
        var gitIn = new Dictionary<(int MaterialId, int MfgMonth), decimal>();
        foreach (var batch in batches.Where(b => b.StockSubtypeId == LocationSubtypeIds.Good))
        {
            var (_, part) = StockFlowStatus.Split(batch.QuantityBoxes, pending.GetValueOrDefault((batch.MaterialId, batch.MfgMonth, batch.PalletPositionId)));
            if (part > 0) gitIn[(batch.MaterialId, batch.MfgMonth)] = gitIn.GetValueOrDefault((batch.MaterialId, batch.MfgMonth)) + part;
        }

        var picks = await StockFlowStatus.OpenPicksAsync(db, filter.WarehouseId, cancellationToken);
        var gitOut = picks.GroupBy(p => (p.MaterialId, p.MfgMonth)).ToDictionary(g => g.Key, g => g.Sum(p => p.QuantityBoxes));

        var rows = batches
            .GroupBy(batch => new
            {
                batch.MaterialId,
                batch.MaterialNumber,
                batch.MaterialDescription,
                batch.MfgMonth,
                batch.MrpPrice
            })
            .Select(group =>
            {
                var total = group.Sum(batch => batch.QuantityBoxes);
                var inbound = gitIn.GetValueOrDefault((group.Key.MaterialId, group.Key.MfgMonth));
                return new MaterialStockReportRowDto(
                    group.Key.MaterialNumber, group.Key.MaterialDescription, group.Key.MfgMonth, group.Key.MrpPrice, total,
                    total - inbound, inbound, gitOut.GetValueOrDefault((group.Key.MaterialId, group.Key.MfgMonth)));
            })
            .ToList();

        // A material + PKM whose boxes are all in unconfirmed picks has no stock row any more (the books deducted it) but is still on the rack.
        var seen = rows.Select(r => (r.MaterialCode, r.Pkm)).ToHashSet();
        var extra = gitOut.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
        if (extra.Count > 0)
        {
            var ids = extra.Select(k => k.MaterialId).Distinct().ToList();
            var materials = await db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id))
                .Select(m => new { m.Id, m.MaterialNumber, m.Description, m.MrpPrice }).ToDictionaryAsync(m => m.Id, cancellationToken);
            foreach (var (materialId, pkm) in extra)
            {
                if (!materials.TryGetValue(materialId, out var m) || seen.Contains((m.MaterialNumber, pkm))) continue;
                if (filter.PkmFrom.HasValue && pkm < filter.PkmFrom.Value) continue;
                if (filter.PkmTo.HasValue && pkm > filter.PkmTo.Value) continue;
                if (filter.MinMrp.HasValue && m.MrpPrice < filter.MinMrp.Value) continue;
                if (filter.MaxMrp.HasValue && m.MrpPrice > filter.MaxMrp.Value) continue;
                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    var term = filter.Search.Trim();
                    if (!(m.MaterialNumber.ToString().Contains(term) || m.Description.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                }

                rows.Add(new MaterialStockReportRowDto(m.MaterialNumber, m.Description, pkm, m.MrpPrice, 0, 0, 0, gitOut[(materialId, pkm)]));
            }
        }

        IEnumerable<MaterialStockReportRowDto> filtered = rows;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            filtered = filter.Status.Trim().ToUpperInvariant() switch
            {
                StockFlowStatus.InStock => filtered.Where(r => r.InStockBoxes > 0),
                StockFlowStatus.GitIn => filtered.Where(r => r.GitInBoxes > 0),
                StockFlowStatus.GitOut => filtered.Where(r => r.GitOutBoxes > 0),
                _ => filtered
            };
        }

        rows = filtered.ToList();

        if (filter.MinBoxes.HasValue)
            rows = rows.Where(row => row.BoxQuantity >= filter.MinBoxes.Value).ToList();
        if (filter.MaxBoxes.HasValue)
            rows = rows.Where(row => row.BoxQuantity <= filter.MaxBoxes.Value).ToList();

        return rows
            .OrderBy(row => row.MaterialCode)
            .ThenBy(row => row.Pkm)
            .ToList();
    }

    private static void Validate(MaterialStockReportFilter filter)
    {
        ValidatePkm(filter.PkmFrom, "PKM from");
        ValidatePkm(filter.PkmTo, "PKM to");

        if (filter.PkmFrom > filter.PkmTo)
            throw new ArgumentException("PKM from must be before PKM to.");
        if (filter.MinMrp is < 0 || filter.MaxMrp is < 0 || filter.MinBoxes is < 0 || filter.MaxBoxes is < 0)
            throw new ArgumentException("MRP and box quantity filters cannot be negative.");
        if (filter.MinMrp > filter.MaxMrp)
            throw new ArgumentException("Minimum MRP cannot exceed maximum MRP.");
        if (filter.MinBoxes > filter.MaxBoxes)
            throw new ArgumentException("Minimum box quantity cannot exceed maximum box quantity.");
    }

    private static void ValidatePkm(int? pkm, string label)
    {
        if (pkm is int value && (value / 100 < 1900 || value % 100 is < 1 or > 12))
            throw new ArgumentException($"{label} must be a valid month.");
    }
}
