using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

/// <summary>The locations holding Good stock of a material in the pullout's warehouse (oldest PKM first): where a supervisor can add that material from.</summary>
[RequiresPermission(PermissionKeys.Pullout.Edit)]
public sealed record GetPulloutAddOptionsQuery(int PulloutId, int MaterialId) : IRequest<IReadOnlyList<PulloutAddOptionDto>>;

public sealed class GetPulloutAddOptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutAddOptionsQuery, IReadOnlyList<PulloutAddOptionDto>>
{
    public async Task<IReadOnlyList<PulloutAddOptionDto>> Handle(GetPulloutAddOptionsQuery request, CancellationToken cancellationToken)
    {
        var warehouseId = await db.PulloutTransactions.AsNoTracking()
            .Where(t => t.Id == request.PulloutId).Select(t => (int?)t.WarehouseId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.PulloutId);
        return await AddPulloutPickHandler.OptionsAsync(db, warehouseId, request.MaterialId, cancellationToken);
    }
}
