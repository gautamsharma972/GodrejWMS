using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.SkuMovementTypes.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.SkuMovementTypes.Queries;

/// <summary>Lists the fixed set of SKU movement types (Fast-moving/Slow-moving), including inactive ones, for admin editing and dropdown population.</summary>
public sealed record GetSkuMovementTypesQuery : IRequest<IReadOnlyList<SkuMovementTypeDto>>;

public sealed class GetSkuMovementTypesHandler(IApplicationDbContext db)
    : IRequestHandler<GetSkuMovementTypesQuery, IReadOnlyList<SkuMovementTypeDto>>
{
    public async Task<IReadOnlyList<SkuMovementTypeDto>> Handle(GetSkuMovementTypesQuery request, CancellationToken cancellationToken)
    {
        return await db.SkuMovementTypes
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName)
            .Select(s => new SkuMovementTypeDto(s.Id, s.Code, s.DisplayName, s.Description, s.SortOrder, s.IsActive))
            .ToListAsync(cancellationToken);
    }
}
