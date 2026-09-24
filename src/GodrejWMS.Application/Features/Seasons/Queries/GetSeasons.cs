using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Seasons.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Seasons.Queries;

/// <summary>Lists the fixed set of seasons (Rainy/Summer/Winter), including inactive ones, for admin editing and dropdown population.</summary>
public sealed record GetSeasonsQuery : IRequest<IReadOnlyList<SeasonDto>>;

public sealed class GetSeasonsHandler(IApplicationDbContext db)
    : IRequestHandler<GetSeasonsQuery, IReadOnlyList<SeasonDto>>
{
    public async Task<IReadOnlyList<SeasonDto>> Handle(GetSeasonsQuery request, CancellationToken cancellationToken)
    {
        return await db.Seasons
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName)
            .Select(s => new SeasonDto(s.Id, s.Code, s.DisplayName, s.Description, s.SortOrder, s.IsActive))
            .ToListAsync(cancellationToken);
    }
}
