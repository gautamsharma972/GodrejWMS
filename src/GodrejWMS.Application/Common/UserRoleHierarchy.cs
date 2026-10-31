using GodrejWMS.Application.Common.Interfaces;

namespace GodrejWMS.Application.Common;

/// <summary>
/// Central server-side guard for client user-management commands. Platform Admin accounts are
/// vendor-owned and are managed only through the dedicated platform administration workflow.
/// Other accounts may only be managed by a caller above them in the role hierarchy.
/// </summary>
public static class UserRoleHierarchy
{
    public static string? ValidateTarget(ICurrentUserService caller, IReadOnlyList<string> targetRoles)
    {
        if (targetRoles.Contains(KnownRoles.PlatformAdmin, StringComparer.OrdinalIgnoreCase))
        {
            return "Platform Admin accounts cannot be managed here.";
        }

        if (targetRoles.Contains(KnownRoles.Superadmin, StringComparer.OrdinalIgnoreCase)
            && !caller.IsInRole(KnownRoles.PlatformAdmin))
        {
            return "Only Platform Admin can manage a Superadmin account.";
        }

        if (targetRoles.Contains(KnownRoles.LogisticManager, StringComparer.OrdinalIgnoreCase)
            && !caller.IsInRole(KnownRoles.PlatformAdmin)
            && !caller.IsInRole(KnownRoles.Superadmin))
        {
            return "Only Superadmin can manage a Logistic Manager account.";
        }

        if (targetRoles.Contains(KnownRoles.HeadSupervisor, StringComparer.OrdinalIgnoreCase)
            && !caller.IsInRole(KnownRoles.PlatformAdmin)
            && !caller.IsInRole(KnownRoles.Superadmin)
            && !caller.IsInRole(KnownRoles.LogisticManager))
        {
            return "You cannot manage an account above your role level.";
        }

        return null;
    }
}
