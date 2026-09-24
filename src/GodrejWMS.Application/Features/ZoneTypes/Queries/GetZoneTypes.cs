using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.ZoneTypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.ZoneTypes.Queries;

/// <summary>Lists the fixed set of zone types (Fast/Reserve/Seasonal/Dispatch-near), including inactive ones, for admin editing and dropdown population.</summary>
public sealed record GetZoneTypesQuery : IRequest<IReadOnlyList<ZoneTypeDto>>;

public sealed class GetZoneTypesHandler(IApplicationDbContext db)
    : IRequestHandler<GetZoneTypesQuery, IReadOnlyList<ZoneTypeDto>>
{
    public async Task<IReadOnlyList<ZoneTypeDto>> Handle(GetZoneTypesQuery request, CancellationToken cancellationToken)
    {
        return await db.ZoneTypes
            .AsNoTracking()
            .OrderBy(z => z.SortOrder)
            .ThenBy(z => z.DisplayName)
            .Select(z => new ZoneTypeDto(z.Id, z.Code, z.DisplayName, z.Description, z.SortOrder, z.IsActive))
            .ToListAsync(cancellationToken);
    }
}
