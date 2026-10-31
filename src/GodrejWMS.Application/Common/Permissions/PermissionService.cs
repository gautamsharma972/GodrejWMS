using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Common.Permissions;

/// <summary>Scoped (per Blazor Server circuit / per request), so this caches the current user's
/// granted keys after the first check: Web pages render several independent PermissionAuthorize
/// components at once (e.g. NavMenu), often alongside the page's own content query running on the
/// shared scoped DbContext. Querying through <see cref="IApplicationDbContextFactory"/> instead of
/// the shared IApplicationDbContext gives this its own short-lived context, so it never races a
/// concurrent operation on the page's DbContext (EF Core forbids two operations on one context at
/// once). The cache means at most one such query per permission-matrix revision; a successful
/// matrix edit increments the shared revision so existing circuits reload their grants on their
/// next permission check.</summary>
public sealed class PermissionService(
    IApplicationDbContextFactory dbFactory,
    ICurrentUserService currentUser,
    PermissionRevision revision) : IPermissionService
{
    private Task<HashSet<string>>? _grantedKeysTask;
    private long _loadedRevision = -1;
    private readonly object _cacheLock = new();

    public async Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default)
    {
        Task<HashSet<string>> grantedKeysTask;
        lock (_cacheLock)
        {
            var currentRevision = revision.Value;
            if (_grantedKeysTask is null || _loadedRevision != currentRevision)
            {
                _loadedRevision = currentRevision;
                _grantedKeysTask = LoadGrantedKeysAsync(CancellationToken.None);
            }

            grantedKeysTask = _grantedKeysTask;
        }

        var granted = await grantedKeysTask.WaitAsync(cancellationToken);
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
