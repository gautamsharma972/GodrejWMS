using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Queries;

/// <summary>Live Inventory Master grid, matching "Format inventory master": Material, Design Type, Qty, Mfg Month, Pallet Position.</summary>
public sealed record GetStockMasterQuery(
    string? MaterialSearch = null,
    string? RackCode = null,
    string? DesignType = null,
    int? MovementTypeId = null,
    int? SeasonId = null,
    bool? IsActive = null,
    string SortBy = "material",
    bool SortDescending = false,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PaginatedList<StockMasterRowDto>>;

public sealed class GetStockMasterHandler(IApplicationDbContext db)
    : IRequestHandler<GetStockMasterQuery, PaginatedList<StockMasterRowDto>>
{
    public async Task<PaginatedList<StockMasterRowDto>> Handle(GetStockMasterQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockBatches
            .AsNoTracking()
            .Where(b => b.QuantityBoxes > 0)
            .Select(b => new
            {
                b.Material.MaterialNumber,
                b.Material.Description,
                b.Material.DesignType,
                b.Material.MovementTypeId,
                MovementTypeCode = b.Material.MovementType.Code,
                MovementTypeName = b.Material.MovementType.DisplayName,
                b.Material.SeasonId,
                SeasonCode = b.Material.Season.Code,
                SeasonName = b.Material.Season.DisplayName,
                b.Material.IsActive,
                b.QuantityBoxes,
                b.MfgMonth,
                b.PalletPosition.LocationCode,
                RackCode = b.PalletPosition.Rack.Code
            });

        if (!string.IsNullOrWhiteSpace(request.MaterialSearch))
        {
            var term = request.MaterialSearch.Trim();
            query = query.Where(b =>
                b.Description.Contains(term) || b.MaterialNumber.ToString().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.DesignType))
        {
            query = query.Where(b => b.DesignType == request.DesignType);
        }

        if (request.MovementTypeId.HasValue)
        {
            query = query.Where(b => b.MovementTypeId == request.MovementTypeId.Value);
        }

        if (request.SeasonId.HasValue)
        {
            query = query.Where(b => b.SeasonId == request.SeasonId.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(b => b.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.RackCode))
        {
            query = query.Where(b => b.RackCode == request.RackCode);
        }

        var ordered = (request.SortBy, request.SortDescending) switch
        {
            ("description", false) => query.OrderBy(b => b.Description).ThenBy(b => b.MaterialNumber),
            ("description", true) => query.OrderByDescending(b => b.Description).ThenBy(b => b.MaterialNumber),
            ("design", false) => query.OrderBy(b => b.DesignType).ThenBy(b => b.MaterialNumber),
            ("design", true) => query.OrderByDescending(b => b.DesignType).ThenBy(b => b.MaterialNumber),
            ("quantity", false) => query.OrderBy(b => b.QuantityBoxes).ThenBy(b => b.MaterialNumber),
            ("quantity", true) => query.OrderByDescending(b => b.QuantityBoxes).ThenBy(b => b.MaterialNumber),
            ("mfg", false) => query.OrderBy(b => b.MfgMonth).ThenBy(b => b.MaterialNumber),
            ("mfg", true) => query.OrderByDescending(b => b.MfgMonth).ThenBy(b => b.MaterialNumber),
            ("location", false) => query.OrderBy(b => b.LocationCode).ThenBy(b => b.MaterialNumber),
            ("location", true) => query.OrderByDescending(b => b.LocationCode).ThenBy(b => b.MaterialNumber),
            ("velocity", false) => query.OrderBy(b => b.MovementTypeId).ThenBy(b => b.MaterialNumber),
            ("velocity", true) => query.OrderByDescending(b => b.MovementTypeId).ThenBy(b => b.MaterialNumber),
            ("season", false) => query.OrderBy(b => b.SeasonId).ThenBy(b => b.MaterialNumber),
            ("season", true) => query.OrderByDescending(b => b.SeasonId).ThenBy(b => b.MaterialNumber),
            ("active", false) => query.OrderBy(b => b.IsActive).ThenBy(b => b.MaterialNumber),
            ("active", true) => query.OrderByDescending(b => b.IsActive).ThenBy(b => b.MaterialNumber),
            (_, true) => query.OrderByDescending(b => b.MaterialNumber).ThenBy(b => b.MfgMonth).ThenBy(b => b.LocationCode),
            _ => query.OrderBy(b => b.MaterialNumber).ThenBy(b => b.MfgMonth).ThenBy(b => b.LocationCode)
        };

        var totalCount = await ordered.CountAsync(cancellationToken);

        var page = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // Mfg-month label formatting is done client-side after materialization — EF Core cannot
        // translate the "MMM|yyyy" formatting helper into SQL.
        var items = page
            .Select(b => new StockMasterRowDto(
                b.MaterialNumber, b.Description, b.DesignType, b.QuantityBoxes,
                MfgMonthParser.Format(b.MfgMonth), b.LocationCode, b.MovementTypeCode, b.MovementTypeName, b.SeasonCode, b.SeasonName, b.IsActive))
            .ToList();

        return new PaginatedList<StockMasterRowDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
