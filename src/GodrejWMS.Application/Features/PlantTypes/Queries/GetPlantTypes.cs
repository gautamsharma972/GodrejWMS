using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.PlantTypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.PlantTypes.Queries;

/// <summary>Lists plant types (e.g. CFA, RWH, Plant), including inactive ones, for admin editing and dropdown population.</summary>
public sealed record GetPlantTypesQuery : IRequest<IReadOnlyList<PlantTypeDto>>;

public sealed class GetPlantTypesHandler(IApplicationDbContext db)
    : IRequestHandler<GetPlantTypesQuery, IReadOnlyList<PlantTypeDto>>
{
    public async Task<IReadOnlyList<PlantTypeDto>> Handle(GetPlantTypesQuery request, CancellationToken cancellationToken)
    {
        return await db.PlantTypes
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Select(p => new PlantTypeDto(p.Id, p.Code, p.Name, p.SortOrder, p.IsActive))
            .ToListAsync(cancellationToken);
    }
}
