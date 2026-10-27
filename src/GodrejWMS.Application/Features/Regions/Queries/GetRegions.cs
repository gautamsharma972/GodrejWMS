using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Regions.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Regions.Queries;

public sealed record GetRegionsQuery : IRequest<IReadOnlyList<RegionDto>>;

public sealed class GetRegionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetRegionsQuery, IReadOnlyList<RegionDto>>
{
    public async Task<IReadOnlyList<RegionDto>> Handle(GetRegionsQuery request, CancellationToken cancellationToken) =>
        await db.Regions.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RegionDto(r.Id, r.Code, r.Name, r.Description, r.IsActive))
            .ToListAsync(cancellationToken);
}
