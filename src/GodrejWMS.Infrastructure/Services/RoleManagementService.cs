using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

public sealed class RoleManagementService(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IApplicationDbContext db) : IRoleManagementService
{
    public async Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken cancellationToken)
    {
        // Platform Admin is a fixed, vendor-only role - it exists as a real AspNetRoles row (so
        // login/claims work) but is never part of the client-visible "every role that exists"
        // list this feeds (the Role & Permission Management matrix columns and the Users page's
        // role-assignment dropdown), so it's unconditionally excluded here rather than filtered
        // per-caller.
        var all = await roleManager.Roles.Select(r => r.Name!).Where(n => n != RoleNames.PlatformAdmin).ToListAsync(cancellationToken);
        var builtInOrder = RoleNames.All;
        var builtIn = builtInOrder.Where(all.Contains);
        var custom = all.Except(builtInOrder, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);
        return builtIn.Concat(custom).ToList();
    }

    public async Task CreateRoleAsync(string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (trimmed == RoleNames.PlatformAdmin)
        {
            throw new InvalidOperationException("That role name is reserved.");
        }
        if (await roleManager.RoleExistsAsync(trimmed))
        {
            throw new InvalidOperationException($"A role named \"{trimmed}\" already exists.");
        }

        var result = await roleManager.CreateAsync(new IdentityRole(trimmed));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        db.RolePermissions.AddRange(PermissionKeys.All.Select(key =>
            new RolePermission { RoleName = trimmed, PermissionKey = key, IsGranted = false }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRoleAsync(string name, CancellationToken cancellationToken)
    {
        if (KnownRoles.All.Contains(name) || name == KnownRoles.PlatformAdmin)
        {
            throw new InvalidOperationException($"\"{name}\" is a built-in role and cannot be deleted.");
        }

        var role = await roleManager.FindByNameAsync(name)
            ?? throw new InvalidOperationException($"Role \"{name}\" was not found.");

        var usersInRole = await userManager.GetUsersInRoleAsync(name);
        if (usersInRole.Count > 0)
        {
            throw new InvalidOperationException(
                $"\"{name}\" is still assigned to {usersInRole.Count} user(s). Reassign them before deleting this role.");
        }

        var grants = await db.RolePermissions.Where(p => p.RoleName == name).ToListAsync(cancellationToken);
        db.RolePermissions.RemoveRange(grants);

        var result = await roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
