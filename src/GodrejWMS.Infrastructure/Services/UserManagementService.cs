using System.Security.Cryptography;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GodrejWMS.Infrastructure.Services;

public sealed class UserManagementService(UserManager<ApplicationUser> userManager) : IUserManagementService
{
    public async Task<IReadOnlyList<UserAccountDto>> ListUsersAsync(CancellationToken cancellationToken)
    {
        // Platform Admin accounts (vendor-only) never appear in the client-facing Users page,
        // regardless of who's viewing it - including another Platform Admin logged in and looking
        // at that page. The hidden /platform-admin screen uses ListAllUsersAsync instead.
        var all = await ListAllUsersAsync(cancellationToken);
        return all.Where(u => !u.Roles.Contains(RoleNames.PlatformAdmin)).ToList();
    }

    public async Task<IReadOnlyList<UserAccountDto>> ListAllUsersAsync(CancellationToken cancellationToken)
    {
        var users = userManager.Users.ToList();
        var result = new List<UserAccountDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(new UserAccountDto(user.Id, user.Email ?? user.UserName ?? string.Empty, user.FullName, user.IsActive, roles.ToList()));
        }
        return result.OrderBy(u => u.Email).ToList();
    }

    public async Task<UserAccountDto?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }
        var roles = await userManager.GetRolesAsync(user);
        return new UserAccountDto(user.Id, user.Email ?? user.UserName ?? string.Empty, user.FullName, user.IsActive, roles.ToList());
    }

    public async Task<(string UserId, string TemporaryPassword)> CreateUserAsync(
        string email, string fullName, string role, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true
        };

        var temporaryPassword = GenerateTemporaryPassword();
        var createResult = await userManager.CreateAsync(user, temporaryPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        return (user.Id, temporaryPassword);
    }

    public async Task SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");
        user.IsActive = isActive;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // Deactivating a user shouldn't leave their existing login session valid - bump the
        // security stamp so IdentityRevalidatingAuthenticationStateProvider's periodic check
        // signs them out within its revalidation window.
        if (!isActive)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }
    }

    public async Task UpdateUserAsync(string userId, string email, string fullName, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        user.Email = email;
        user.UserName = email;
        user.FullName = fullName;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task DeleteUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task SetRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", removeResult.Errors.Select(e => e.Description)));
            }
        }

        var addResult = await userManager.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", addResult.Errors.Select(e => e.Description)));
        }

        // A role change invalidates the old session's authorization just as much as a password
        // reset does - force re-authentication so the new role takes effect promptly instead of
        // waiting out the periodic revalidation window.
        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<string> ResetPasswordAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var temporaryPassword = GenerateTemporaryPassword();
        var result = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // Invalidate any existing session(s) for this account, per spec 14.3 ("invalidate existing
        // sessions or authentication tokens after a password reset where supported").
        await userManager.UpdateSecurityStampAsync(user);

        return temporaryPassword;
    }

    private static string GenerateTemporaryPassword()
    {
        // Meets the app's configured Identity password policy (min length 8, non-alphanumeric not
        // required - see AddInfrastructure) with margin: a random GUID-derived string is long,
        // mixed-case and digit-containing by construction, then a symbol is appended so it also
        // satisfies a stricter policy if that's ever tightened later.
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12))
            .Replace("+", "A").Replace("/", "B").Replace("=", "C");
        return $"{raw}!1";
    }
}
