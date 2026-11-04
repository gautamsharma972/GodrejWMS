using System.Security.Claims;
using GodrejWMS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.PickersApi.Authorization;

/// <summary>Resolves a PermissionRequirement against the Role &amp; Permission Management matrix -
/// the same data the Blazor app's IPermissionService reads, so a JWT-authenticated API call and the
/// equivalent Razor page are always driven by identical grants. Reads role claims straight off the
/// already-authenticated ClaimsPrincipal the JWT bearer middleware hands us, and queries
/// RolePermissions through the isolated IApplicationDbContextFactory (not a scoped DbContext), same
/// as the Blazor app's handler.
/// Deliberate copy of GodrejWMS.Web.Authorization.PermissionAuthorizationHandler so this API
/// project never references GodrejWMS.Web (see the Native Android Pickers app plan).</summary>
public sealed class PermissionAuthorizationHandler(IApplicationDbContextFactory dbFactory)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var granted = await db.RolePermissions
            .AnyAsync(p => roles.Contains(p.RoleName) && p.PermissionKey == requirement.PermissionKey && p.IsGranted);

        if (granted)
        {
            context.Succeed(requirement);
        }
    }
}
