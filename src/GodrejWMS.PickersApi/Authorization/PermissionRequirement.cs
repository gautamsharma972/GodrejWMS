using Microsoft.AspNetCore.Authorization;

namespace GodrejWMS.PickersApi.Authorization;

/// <summary>Authorization requirement satisfied when the current user holds the given permission
/// key per the Role &amp; Permission Management matrix - see <see cref="PermissionPolicyProvider"/>
/// for how "Permission:&lt;key&gt;" policy names turn into one of these.
/// Deliberate copy of GodrejWMS.Web.Authorization.PermissionRequirement so this API project never
/// references GodrejWMS.Web (see the Native Android Pickers app plan).</summary>
public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
