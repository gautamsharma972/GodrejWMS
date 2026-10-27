using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Cities.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Cities.Queries;

public sealed record GetCitiesQuery(int? StateId = null) : IRequest<IReadOnlyList<CityDto>>;

public sealed class GetCitiesHandler(IApplicationDbContext db)
    : IRequestHandler<GetCitiesQuery, IReadOnlyList<CityDto>>
{
    public async Task<IReadOnlyList<CityDto>> Handle(GetCitiesQuery request, CancellationToken cancellationToken) =>
        await db.Cities.AsNoTracking()
            .Where(c => request.StateId == null || c.StateId == request.StateId)
            .OrderBy(c => c.Name)
            .Select(c => new CityDto(
                c.Id, c.Code, c.Name,
                c.StateId, c.State.Code, c.State.Name,
                c.State.RegionId, c.State.Region.Code, c.State.Region.Name,
                c.IsActive))
            .ToListAsync(cancellationToken);
}
