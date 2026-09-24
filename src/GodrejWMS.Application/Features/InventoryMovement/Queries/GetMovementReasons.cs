using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryMovement.Queries;

/// <summary>Lists Inventory Movement reasons for dropdown population (§19).</summary>
public sealed record GetMovementReasonsQuery : IRequest<IReadOnlyList<MovementReasonDto>>;

public sealed class GetMovementReasonsHandler(IApplicationDbContext db)
    : IRequestHandler<GetMovementReasonsQuery, IReadOnlyList<MovementReasonDto>>
{
    public async Task<IReadOnlyList<MovementReasonDto>> Handle(GetMovementReasonsQuery request, CancellationToken cancellationToken)
    {
        return await db.MovementReasons
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.DisplayName)
            .Select(r => new MovementReasonDto(r.Id, r.Code, r.DisplayName, r.IsActive))
            .ToListAsync(cancellationToken);
    }
}
