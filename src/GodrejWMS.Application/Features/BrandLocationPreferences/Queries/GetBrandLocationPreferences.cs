using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.BrandLocationPreferences.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.BrandLocationPreferences.Queries;

public sealed record GetBrandLocationPreferencesQuery : IRequest<IReadOnlyList<BrandLocationPreferenceDto>>;

public sealed class GetBrandLocationPreferencesHandler(IApplicationDbContext db)
    : IRequestHandler<GetBrandLocationPreferencesQuery, IReadOnlyList<BrandLocationPreferenceDto>>
{
    public async Task<IReadOnlyList<BrandLocationPreferenceDto>> Handle(
        GetBrandLocationPreferencesQuery request, CancellationToken cancellationToken) =>
        await db.BrandLocationPreferences.AsNoTracking()
            .OrderBy(x => x.Brand.Code)
            .ThenBy(x => x.FromPalletPosition.Rack.Warehouse.Code)
            .ThenBy(x => x.FromPalletPosition.Rack.Code)
            .ThenBy(x => x.FromPalletPosition.Column)
            .ThenBy(x => x.FromPalletPosition.Level)
            .Select(x => new BrandLocationPreferenceDto(
                x.Id,
                x.BrandId,
                x.Brand.Code,
                x.FromPalletPosition.Rack.WarehouseId,
                x.FromPalletPosition.Rack.Warehouse.Code,
                x.FromPalletPosition.RackId,
                x.FromPalletPosition.Rack.Code,
                x.FromPalletPositionId,
                x.FromPalletPosition.LocationCode,
                x.ToPalletPositionId,
                x.ToPalletPosition.LocationCode,
                x.IsActive))
            .ToListAsync(cancellationToken);
}

public sealed record GetBrandLocationOptionsQuery : IRequest<IReadOnlyList<BrandLocationOptionDto>>;

public sealed class GetBrandLocationOptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetBrandLocationOptionsQuery, IReadOnlyList<BrandLocationOptionDto>>
{
    public async Task<IReadOnlyList<BrandLocationOptionDto>> Handle(
        GetBrandLocationOptionsQuery request, CancellationToken cancellationToken) =>
        await db.PalletPositions.AsNoTracking()
            .Where(x => x.IsActive && x.Rack.IsActive && x.Rack.Warehouse.IsActive)
            .OrderBy(x => x.Rack.Warehouse.Code)
            .ThenBy(x => x.Rack.Code)
            .ThenBy(x => x.Column)
            .ThenBy(x => x.Level)
            .Select(x => new BrandLocationOptionDto(
                x.Id,
                x.Rack.WarehouseId,
                x.Rack.Warehouse.Code,
                x.RackId,
                x.Rack.Code,
                x.Column,
                x.Level,
                x.LocationCode))
            .ToListAsync(cancellationToken);
}
