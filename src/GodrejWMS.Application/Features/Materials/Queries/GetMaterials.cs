using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.Materials.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Materials.Queries;

public sealed record GetMaterialsQuery(
    string? Search = null,
    string? DesignType = null,
    int? MovementTypeId = null,
    int? SeasonId = null,
    bool? IsActive = null,
    string SortBy = "material",
    bool SortDescending = false,
    int PageNumber = 1,
    int PageSize = 25) : IRequest<PaginatedList<MaterialDto>>;

public sealed class GetMaterialsHandler(IApplicationDbContext db)
    : IRequestHandler<GetMaterialsQuery, PaginatedList<MaterialDto>>
{
    public Task<PaginatedList<MaterialDto>> Handle(GetMaterialsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Materials.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(m =>
                m.Description.Contains(term) ||
                m.MaterialNumber.ToString().Contains(term) ||
                m.DesignType.Contains(term));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(m => m.IsActive == request.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.DesignType))
        {
            query = query.Where(m => m.DesignType == request.DesignType);
        }

        if (request.MovementTypeId.HasValue)
        {
            query = query.Where(m => m.MovementTypeId == request.MovementTypeId.Value);
        }

        if (request.SeasonId.HasValue)
        {
            query = query.Where(m => m.SeasonId == request.SeasonId.Value);
        }

        var ordered = (request.SortBy, request.SortDescending) switch
        {
            ("description", false) => query.OrderBy(m => m.Description).ThenBy(m => m.MaterialNumber),
            ("description", true) => query.OrderByDescending(m => m.Description).ThenBy(m => m.MaterialNumber),
            ("design", false) => query.OrderBy(m => m.DesignType).ThenBy(m => m.MaterialNumber),
            ("design", true) => query.OrderByDescending(m => m.DesignType).ThenBy(m => m.MaterialNumber),
            ("pack", false) => query.OrderBy(m => m.PackSize).ThenBy(m => m.MaterialNumber),
            ("pack", true) => query.OrderByDescending(m => m.PackSize).ThenBy(m => m.MaterialNumber),
            ("mrp", false) => query.OrderBy(m => m.MrpPrice).ThenBy(m => m.MaterialNumber),
            ("mrp", true) => query.OrderByDescending(m => m.MrpPrice).ThenBy(m => m.MaterialNumber),
            ("velocity", false) => query.OrderBy(m => m.MovementTypeId).ThenBy(m => m.MaterialNumber),
            ("velocity", true) => query.OrderByDescending(m => m.MovementTypeId).ThenBy(m => m.MaterialNumber),
            ("boxWeight", false) => query.OrderBy(m => m.BoxWeightKg).ThenBy(m => m.MaterialNumber),
            ("boxWeight", true) => query.OrderByDescending(m => m.BoxWeightKg).ThenBy(m => m.MaterialNumber),
            ("palletWeight", false) => query.OrderBy(m => m.PalletWeightKg).ThenBy(m => m.MaterialNumber),
            ("palletWeight", true) => query.OrderByDescending(m => m.PalletWeightKg).ThenBy(m => m.MaterialNumber),
            ("season", false) => query.OrderBy(m => m.SeasonId).ThenBy(m => m.MaterialNumber),
            ("season", true) => query.OrderByDescending(m => m.SeasonId).ThenBy(m => m.MaterialNumber),
            ("active", false) => query.OrderBy(m => m.IsActive).ThenBy(m => m.MaterialNumber),
            ("active", true) => query.OrderByDescending(m => m.IsActive).ThenBy(m => m.MaterialNumber),
            (_, true) => query.OrderByDescending(m => m.MaterialNumber),
            _ => query.OrderBy(m => m.MaterialNumber)
        };

        var projected = ordered.Select(m => new MaterialDto(
                m.Id,
                m.MaterialNumber,
                m.Description,
                m.DesignType,
                m.CharacteristicValue,
                m.PackSize,
                m.MrpPrice,
                m.LengthMm,
                m.WidthMm,
                m.HeightMm,
                m.VolumeMm3,
                m.NetWeightKg,
                m.GrossWeightKg,
                m.BoxWeightKg,
                m.PalletCapacityBoxes,
                m.PalletWeightKg,
                m.MovementTypeId,
                m.MovementType.Code,
                m.MovementType.DisplayName,
                m.SeasonId,
                m.Season.Code,
                m.Season.DisplayName,
                m.PreferredZoneTypeId,
                m.PreferredZoneType == null ? null : m.PreferredZoneType.DisplayName,
                m.RequirePreferredZone,
                m.IsActive));

        return PaginatedList<MaterialDto>.CreateAsync(projected, request.PageNumber, request.PageSize);
    }
}
