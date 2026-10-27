using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Reports.Dtos;
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
    decimal? MaxBoxes = null);

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
                batch.QuantityBoxes
            })
            .ToListAsync(cancellationToken);

        var rows = batches
            .GroupBy(batch => new
            {
                batch.MaterialId,
                batch.MaterialNumber,
                batch.MaterialDescription,
                batch.MfgMonth,
                batch.MrpPrice
            })
            .Select(group => new MaterialStockReportRowDto(
                group.Key.MaterialNumber,
                group.Key.MaterialDescription,
                group.Key.MfgMonth,
                group.Key.MrpPrice,
                group.Sum(batch => batch.QuantityBoxes)));

        if (filter.MinBoxes.HasValue)
            rows = rows.Where(row => row.BoxQuantity >= filter.MinBoxes.Value);
        if (filter.MaxBoxes.HasValue)
            rows = rows.Where(row => row.BoxQuantity <= filter.MaxBoxes.Value);

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
