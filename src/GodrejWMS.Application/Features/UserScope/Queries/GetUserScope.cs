using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.UserScope.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.UserScope.Queries;

/// <summary>Reads a user's current data-access scope: the single warehouse they're directly
/// assigned (Office Staff/Head Supervisor/Supervisor), and/or the regions they're assigned
/// (Logistic Manager - grants every warehouse in those regions). A Superadmin has neither and
/// doesn't need one (organization-wide access, see AppDbContext.HasAllWarehouses).</summary>
public sealed record GetUserScopeQuery(string UserId) : IRequest<UserScopeDto>;

/// <summary>Uses <see cref="IApplicationDbContextFactory"/> rather than the shared scoped
/// IApplicationDbContext: this query backs the topbar's warehouse-scope badge, which renders
/// alongside several other independent Blazor components (NavMenu, GlobalSearch,
/// NotificationCenter) that may each be mid-await on the shared context at the same time - see
/// PermissionService for the same reasoning.</summary>
public sealed class GetUserScopeHandler(IApplicationDbContextFactory dbFactory)
    : IRequestHandler<GetUserScopeQuery, UserScopeDto>
{
    public async Task<UserScopeDto> Handle(GetUserScopeQuery request, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var warehouse = await db.UserWarehouses
            .Where(w => w.UserId == request.UserId)
            .Select(w => new { w.WarehouseId, w.Warehouse.Code, w.Warehouse.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var regions = await db.UserRegions
            .Where(r => r.UserId == request.UserId)
            .Select(r => new UserScopeRegionDto(r.RegionId, r.Region.Code, r.Region.Name))
            .ToListAsync(cancellationToken);

        return new UserScopeDto(request.UserId, warehouse?.WarehouseId, warehouse?.Code, warehouse?.Name, regions);
    }
}
