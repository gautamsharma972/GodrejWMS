using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.LocationSubtypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationSubtypes.Queries;

/// <summary>Lists the fixed set of location/stock subtypes (Good/Damage/Expire/Hold), including inactive ones, for admin editing and dropdown population.</summary>
public sealed record GetLocationSubtypesQuery : IRequest<IReadOnlyList<LocationSubtypeDto>>;

public sealed class GetLocationSubtypesHandler(IApplicationDbContext db)
    : IRequestHandler<GetLocationSubtypesQuery, IReadOnlyList<LocationSubtypeDto>>
{
    public async Task<IReadOnlyList<LocationSubtypeDto>> Handle(GetLocationSubtypesQuery request, CancellationToken cancellationToken)
    {
        return await db.LocationSubtypes
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName)
            .Select(s => new LocationSubtypeDto(s.Id, s.Code, s.DisplayName, s.Description, s.SortOrder, s.IsActive))
            .ToListAsync(cancellationToken);
    }
}
