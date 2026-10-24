using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Plants.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Plants.Queries;

/// <summary>Lists plants, including inactive ones, for admin editing and dropdown population (e.g. the Pullout "Plant Name" selector).</summary>
public sealed record GetPlantsQuery : IRequest<IReadOnlyList<PlantDto>>;

public sealed class GetPlantsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPlantsQuery, IReadOnlyList<PlantDto>>
{
    public async Task<IReadOnlyList<PlantDto>> Handle(GetPlantsQuery request, CancellationToken cancellationToken)
    {
        return await db.Plants
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PlantDto(
                p.Id, p.Code, p.Name, p.PlantTypeId,
                p.PlantType != null ? p.PlantType.Code : null,
                p.PlantType != null ? p.PlantType.Name : null,
                p.Description, p.IsActive))
            .ToListAsync(cancellationToken);
    }
}
