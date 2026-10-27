namespace GodrejWMS.Application.Common.Permissions;

/// <summary>Central permission check - backs both the Web layer's menu/button visibility and
/// Application-layer command/query authorization, so "enforce the same permissions in backend
/// APIs, don't rely on frontend visibility" (spec section 7) is one code path, not two.</summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default);
}
