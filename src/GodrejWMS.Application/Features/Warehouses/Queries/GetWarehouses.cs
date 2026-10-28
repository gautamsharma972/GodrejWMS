using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Warehouses.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Warehouses.Queries;

public sealed record GetWarehousesQuery : IRequest<IReadOnlyList<WarehouseDto>>;

public sealed class GetWarehousesHandler(IApplicationDbContext db)
    : IRequestHandler<GetWarehousesQuery, IReadOnlyList<WarehouseDto>>
{
    public async Task<IReadOnlyList<WarehouseDto>> Handle(GetWarehousesQuery request, CancellationToken cancellationToken) =>
        await db.Warehouses.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WarehouseDto(
                w.Id, w.Code, w.Name,
                w.WarehouseCategoryId, w.WarehouseCategory != null ? w.WarehouseCategory.Code : null, w.WarehouseCategory != null ? w.WarehouseCategory.Name : null,
                w.CityId, w.City != null ? w.City.Name : null,
                w.City != null ? w.City.StateId : null, w.City != null ? w.City.State.Name : null,
                w.City != null ? w.City.State.RegionId : null, w.City != null ? w.City.State.Region.Name : null,
                w.Address,
                w.AllowPkmMixing,
                w.IsActive))
            .ToListAsync(cancellationToken);
}
