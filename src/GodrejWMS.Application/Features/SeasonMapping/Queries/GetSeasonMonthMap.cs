using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.SeasonMapping.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.SeasonMapping.Queries;

public sealed record GetSeasonMonthMapQuery : IRequest<IReadOnlyList<SeasonMonthMapDto>>;

public sealed class GetSeasonMonthMapHandler(IApplicationDbContext db)
    : IRequestHandler<GetSeasonMonthMapQuery, IReadOnlyList<SeasonMonthMapDto>>
{
    public async Task<IReadOnlyList<SeasonMonthMapDto>> Handle(GetSeasonMonthMapQuery request, CancellationToken cancellationToken)
    {
        return await db.SeasonMonthMaps
            .AsNoTracking()
            .OrderBy(s => s.Month)
            .Select(s => new SeasonMonthMapDto(s.Id, s.Month, s.SeasonId, s.Season.Code, s.Season.DisplayName))
            .ToListAsync(cancellationToken);
    }
}
