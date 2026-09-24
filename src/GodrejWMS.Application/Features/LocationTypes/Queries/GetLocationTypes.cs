using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.LocationTypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationTypes.Queries;

/// <summary>Lists the fixed set of location types (Rack/Pallet/Floor/Yard), including inactive ones, for admin editing and dropdown population.</summary>
public sealed record GetLocationTypesQuery : IRequest<IReadOnlyList<LocationTypeDto>>;

public sealed class GetLocationTypesHandler(IApplicationDbContext db)
    : IRequestHandler<GetLocationTypesQuery, IReadOnlyList<LocationTypeDto>>
{
    public async Task<IReadOnlyList<LocationTypeDto>> Handle(GetLocationTypesQuery request, CancellationToken cancellationToken)
    {
        return await db.LocationTypes
            .AsNoTracking()
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.DisplayName)
            .Select(l => new LocationTypeDto(l.Id, l.Code, l.DisplayName, l.Description, l.SortOrder, l.IsActive))
            .ToListAsync(cancellationToken);
    }
}
