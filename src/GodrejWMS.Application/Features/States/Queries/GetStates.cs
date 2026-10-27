using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.States.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.States.Queries;

/// <summary>Lists states. <paramref name="RegionId"/> narrows to one region (e.g. when displaying
/// only the regions permitted by the logged-in user's access scope - see GetAccessibleWarehouses
/// for the equivalent existing pattern).</summary>
public sealed record GetStatesQuery(int? RegionId = null) : IRequest<IReadOnlyList<StateDto>>;

public sealed class GetStatesHandler(IApplicationDbContext db)
    : IRequestHandler<GetStatesQuery, IReadOnlyList<StateDto>>
{
    public async Task<IReadOnlyList<StateDto>> Handle(GetStatesQuery request, CancellationToken cancellationToken) =>
        await db.States.AsNoTracking()
            .Where(s => request.RegionId == null || s.RegionId == request.RegionId)
            .OrderBy(s => s.Name)
            .Select(s => new StateDto(s.Id, s.Code, s.Name, s.RegionId, s.Region.Code, s.Region.Name, s.IsActive))
            .ToListAsync(cancellationToken);
}
