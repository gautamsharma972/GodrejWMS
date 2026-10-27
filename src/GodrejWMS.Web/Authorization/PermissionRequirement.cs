using Microsoft.AspNetCore.Authorization;

namespace GodrejWMS.Web.Authorization;

/// <summary>Authorization requirement satisfied when the current user holds the given permission
/// key per the Role &amp; Permission Management matrix - see <see cref="PermissionPolicyProvider"/>
/// for how "Permission:&lt;key&gt;" policy names turn into one of these.</summary>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
