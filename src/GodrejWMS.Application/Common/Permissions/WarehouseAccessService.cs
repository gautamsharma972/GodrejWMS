using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Common.Permissions;

/// <summary>Scoped (per Blazor Server circuit / per request) - see <see cref="PermissionService"/>
/// for why this goes through <see cref="IApplicationDbContextFactory"/> rather than the shared
/// scoped IApplicationDbContext: warehouse pickers render as independent UI components that can
/// fire alongside whatever the page's own handler is doing on the shared context, and this is
/// also called from command validators where an isolated context is just as safe. Caches the
/// resolved set for the life of the scope, same tradeoff as PermissionService.</summary>
public sealed class WarehouseAccessService(IApplicationDbContextFactory dbFactory, ICurrentUserService currentUser) : IWarehouseAccessService
{
    private Task<IReadOnlyList<WarehouseOption>>? _accessibleWarehousesTask;

    public async Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default)
    {
        _accessibleWarehousesTask ??= LoadAccessibleWarehousesAsync(cancellationToken);
        return await _accessibleWarehousesTask;
    }

    public async Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        var warehouses = await GetAccessibleWarehousesAsync(cancellationToken);
        return warehouses.Any(w => w.Id == warehouseId);
    }

    private async Task<IReadOnlyList<WarehouseOption>> LoadAccessibleWarehousesAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var isSuperadmin = currentUser.IsPlatformLevel();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (isSuperadmin || userId is null)
        {
            // No userId (e.g. not authenticated) falls through to the same "see nothing scoped"
            // behavior as every read-side query filter already gives an anonymous/unscoped caller:
            // an empty list for a non-Superadmin, every active warehouse for a Superadmin.
            if (!isSuperadmin)
            {
                return [];
            }

            return await db.Warehouses.AsNoTracking().Where(w => w.IsActive)
                .OrderBy(w => w.Code)
                .Select(w => new WarehouseOption(w.Id, w.Code, w.Name))
                .ToListAsync(cancellationToken);
        }

        return await db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive &&
                (db.UserWarehouses.Any(a => a.UserId == userId && a.WarehouseId == w.Id) ||
                 db.UserRegions.Any(r => r.UserId == userId && w.City != null && w.City.State.RegionId == r.RegionId)))
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseOption(w.Id, w.Code, w.Name))
            .ToListAsync(cancellationToken);
    }
}
