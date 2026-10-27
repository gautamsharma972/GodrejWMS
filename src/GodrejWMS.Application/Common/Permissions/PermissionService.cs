using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Common.Permissions;

/// <summary>Scoped (per Blazor Server circuit / per request), so this caches the current user's
/// granted keys after the first check: Web pages render several independent PermissionAuthorize
/// components at once (e.g. NavMenu), often alongside the page's own content query running on the
/// shared scoped DbContext. Querying through <see cref="IApplicationDbContextFactory"/> instead of
/// the shared IApplicationDbContext gives this its own short-lived context, so it never races a
/// concurrent operation on the page's DbContext (EF Core forbids two operations on one context at
/// once). The cache then means at most one such query per request/circuit; it also means a live
/// Superadmin matrix edit only takes effect for a user on their next circuit (reload/reconnect) -
/// the same staleness tradeoff role changes already have via the security stamp.</summary>
public sealed class PermissionService(IApplicationDbContextFactory dbFactory, ICurrentUserService currentUser) : IPermissionService
{
    private Task<HashSet<string>>? _grantedKeysTask;

    public async Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default)
    {
        _grantedKeysTask ??= LoadGrantedKeysAsync(cancellationToken);
        var granted = await _grantedKeysTask;
        return granted.Contains(permissionKey);
    }

    private async Task<HashSet<string>> LoadGrantedKeysAsync(CancellationToken cancellationToken)
    {
        var roles = currentUser.Roles;
        if (roles.Count == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var keys = await db.RolePermissions
            .Where(p => roles.Contains(p.RoleName) && p.IsGranted)
            .Select(p => p.PermissionKey)
            .ToListAsync(cancellationToken);

        return keys.ToHashSet();
    }
}
