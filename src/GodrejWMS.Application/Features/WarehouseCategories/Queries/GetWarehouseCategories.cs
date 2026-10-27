using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.WarehouseCategories.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.WarehouseCategories.Queries;

public sealed record GetWarehouseCategoriesQuery : IRequest<IReadOnlyList<WarehouseCategoryDto>>;

public sealed class GetWarehouseCategoriesHandler(IApplicationDbContext db)
    : IRequestHandler<GetWarehouseCategoriesQuery, IReadOnlyList<WarehouseCategoryDto>>
{
    public async Task<IReadOnlyList<WarehouseCategoryDto>> Handle(GetWarehouseCategoriesQuery request, CancellationToken cancellationToken) =>
        await db.WarehouseCategories.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WarehouseCategoryDto(w.Id, w.Code, w.Name, w.Description, w.IsActive))
            .ToListAsync(cancellationToken);
}
