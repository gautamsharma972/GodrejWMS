using System.ComponentModel.DataAnnotations;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Identity;
using GodrejWMS.PickersApi.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.PickersApi.Endpoints;

public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public sealed record LoginResponse(
    string AccessToken, DateTimeOffset ExpiresAt, string UserId, string FullName,
    IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/auth/login", async (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            JwtTokenService tokenService,
            IApplicationDbContext db,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            // Mirrors Login.razor's LoginUser: still runs CheckPasswordSignInAsync for a disabled
            // account (so a bad password on a disabled account doesn't leak "account disabled"
            // separately from "wrong password"), then forces the result to NotAllowed.
            var checkResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            var result = user.IsActive ? checkResult : Microsoft.AspNetCore.Identity.SignInResult.NotAllowed;

            if (!result.Succeeded)
            {
                return Results.Unauthorized();
            }

            var token = await tokenService.IssueTokenAsync(user, cancellationToken);
            var roles = await userManager.GetRolesAsync(user);

            // Same lookup PermissionService/PermissionAuthorizationHandler already use elsewhere
            // (RolePermissions rows for the user's roles, IsGranted = true) - computed once here
            // so the mobile app can decide which Home-screen cards to show without a second
            // round trip or re-implementing the permission matrix client-side.
            var roleList = roles.ToList();
            var permissions = await db.RolePermissions
                .Where(p => roleList.Contains(p.RoleName) && p.IsGranted)
                .Select(p => p.PermissionKey)
                .Distinct()
                .ToListAsync(cancellationToken);

            return Results.Ok(new LoginResponse(token.AccessToken, token.ExpiresAt, user.Id, user.FullName, roleList, permissions));
        })
        .AllowAnonymous()
        .WithName("Login");

        return group;
    }
}
