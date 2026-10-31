using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Queries;

public sealed record GetAopDescriptionLocationPreferencesQuery : IRequest<IReadOnlyList<AopDescriptionLocationPreferenceDto>>;

public sealed class GetAopDescriptionLocationPreferencesHandler(IApplicationDbContext db)
    : IRequestHandler<GetAopDescriptionLocationPreferencesQuery, IReadOnlyList<AopDescriptionLocationPreferenceDto>>
{
    public async Task<IReadOnlyList<AopDescriptionLocationPreferenceDto>> Handle(
        GetAopDescriptionLocationPreferencesQuery request, CancellationToken cancellationToken) =>
        await db.AopDescriptionLocationPreferences.AsNoTracking()
            .OrderBy(x => x.AopDescription.Code)
            .ThenBy(x => x.FromPalletPosition.Rack.Warehouse.Code)
            .ThenBy(x => x.FromPalletPosition.Rack.Code)
            .ThenBy(x => x.FromPalletPosition.Column)
            .ThenBy(x => x.FromPalletPosition.Level)
            .Select(x => new AopDescriptionLocationPreferenceDto(
                x.Id,
                x.AopDescriptionId,
                x.AopDescription.Code,
                x.AopDescription.Description,
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

public sealed record GetAopLocationOptionsQuery : IRequest<IReadOnlyList<AopLocationOptionDto>>;

public sealed class GetAopLocationOptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAopLocationOptionsQuery, IReadOnlyList<AopLocationOptionDto>>
{
    public async Task<IReadOnlyList<AopLocationOptionDto>> Handle(
        GetAopLocationOptionsQuery request, CancellationToken cancellationToken) =>
        await db.PalletPositions.AsNoTracking()
            .Where(x => x.IsActive && x.Rack.IsActive && x.Rack.Warehouse.IsActive)
            .OrderBy(x => x.Rack.Warehouse.Code)
            .ThenBy(x => x.Rack.Code)
            .ThenBy(x => x.Column)
            .ThenBy(x => x.Level)
            .Select(x => new AopLocationOptionDto(
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
