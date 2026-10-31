using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.InventoryAdjustments.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryAdjustments.Queries;

public sealed record GetInventoryAdjustmentDetailQuery(int AdjustmentId) : IRequest<InventoryAdjustmentDetailDto>;

public sealed class GetInventoryAdjustmentDetailHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPermissionService permissions)
    : IRequestHandler<GetInventoryAdjustmentDetailQuery, InventoryAdjustmentDetailDto>
{
    public async Task<InventoryAdjustmentDetailDto> Handle(GetInventoryAdjustmentDetailQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Menu.ViewInventoryAdjustments, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to view inventory adjustments.");
        }

        IQueryable<InventoryAdjustment> query = db.InventoryAdjustments.AsNoTracking()
            .Include(a => a.Lines).ThenInclude(l => l.Material)
            .Include(a => a.Lines).ThenInclude(l => l.PhysicalMaterial)
            .Include(a => a.Lines).ThenInclude(l => l.PalletPosition)
            .Include(a => a.Lines).ThenInclude(l => l.StockSubtype);

        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveAdjustment, cancellationToken))
        {
            query = query.Where(a => a.RequestedByUserId == currentUser.UserId);
        }

        var adjustment = await query
            .SingleOrDefaultAsync(a => a.Id == request.AdjustmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(InventoryAdjustment), request.AdjustmentId);
        return InventoryAdjustmentMapper.ToDetail(adjustment);
    }
}
