using GodrejWMS.Application.Common.Interfaces;

namespace GodrejWMS.Application.Common;

public static class CurrentUserServiceExtensions
{
    /// <summary>True for Superadmin or Platform Admin - every place in the app that gives
    /// Superadmin an unscoped, full-access bypass should use this instead of checking
    /// KnownRoles.Superadmin alone, so Platform Admin automatically gets the identical bypass.</summary>
    public static bool IsPlatformLevel(this ICurrentUserService currentUser) =>
        currentUser.IsInRole(KnownRoles.Superadmin) || currentUser.IsInRole(KnownRoles.PlatformAdmin);
}
