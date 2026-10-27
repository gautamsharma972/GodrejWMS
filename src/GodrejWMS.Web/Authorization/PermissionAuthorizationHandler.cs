using System.Security.Claims;
using GodrejWMS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Web.Authorization;

/// <summary>Resolves a PermissionRequirement against the Role &amp; Permission Management matrix -
/// the same data PermissionAuthorize.razor's IPermissionService reads for the nav menu, so a
/// page's [Authorize(Policy="Permission:...")] attribute and its own nav entry are always driven
/// by the identical grants. Can't reuse IPermissionService/ICurrentUserService directly here: this
/// handler runs inside ASP.NET Core's authorization middleware for the initial page request, a
/// plain per-request DI scope, not a live Blazor circuit - and ICurrentUserService's Web
/// implementation depends on ServerAuthenticationStateProvider, which throws if resolved outside
/// an established circuit. Reading role claims straight off the already-authenticated
/// ClaimsPrincipal the middleware hands us, and querying RolePermissions through the same isolated
/// IApplicationDbContextFactory PermissionService itself uses, sidesteps that entirely.</summary>
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
