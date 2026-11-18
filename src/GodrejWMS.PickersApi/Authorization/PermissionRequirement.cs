using Microsoft.AspNetCore.Authorization;

namespace GodrejWMS.PickersApi.Authorization;

/// <summary>Authorization requirement satisfied when the current user holds at least one of the given permission keys per the Role &amp;
/// Permission Management matrix - see <see cref="PermissionPolicyProvider"/> for how "Permission:&lt;key&gt;" policy names (several keys
/// separated by "|" mean "any of") turn into one of these.
/// Deliberate copy of GodrejWMS.Web.Authorization.PermissionRequirement so this API project never references GodrejWMS.Web.</summary>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;

    /// <summary>The key, or each of the "|"-separated keys; holding any one satisfies the requirement.</summary>
    public IReadOnlyList<string> PermissionKeys { get; } =
        permissionKey.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
