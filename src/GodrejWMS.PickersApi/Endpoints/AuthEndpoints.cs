using System.ComponentModel.DataAnnotations;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Identity;
using GodrejWMS.PickersApi.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.PickersApi.Endpoints;

/// <summary>Latitude/Longitude are optional on the wire (older app builds, or a user who denied
/// the location permission, simply omit them) but become required at the point of use below if the
/// signing-in user turns out to hold the Pickers role at a geofenced warehouse.</summary>
public sealed record LoginRequest(
    [Required, EmailAddress] string Email, [Required] string Password, double? Latitude = null, double? Longitude = null);

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

            var roles = await userManager.GetRolesAsync(user);
            var roleList = roles.ToList();

            // Picker-login geofencing (Platform Admin only - see UpdateWarehouseGeofenceCommand):
            // a Pickers-role user may only sign in to the mobile app from within range of one of
            // their assigned warehouses, when that warehouse has geofencing enabled. A user with
            // no geofenced warehouses assigned is unaffected. Checked here, before issuing a token,
            // rather than as a general-purpose authorization rule, since this is specifically a
            // login-time location check, not an ongoing per-request restriction.
            if (roleList.Contains(RoleNames.Pickers))
            {
                var geofences = await db.UserWarehouses
                    .Where(uw => uw.UserId == user.Id)
                    .Join(db.Warehouses, uw => uw.WarehouseId, w => w.Id, (uw, w) => w)
                    .Where(w => w.GeofenceEnabled && w.GeofenceLatitude != null && w.GeofenceLongitude != null && w.GeofenceRadiusMeters != null)
                    .ToListAsync(cancellationToken);

                if (geofences.Count > 0)
                {
                    if (request.Latitude is null || request.Longitude is null)
                    {
                        return Results.Json(
                            new { message = "Location is required to sign in. Please allow location access and try again." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }

                    var withinRange = geofences.Any(w => GeoDistance.MetersBetween(
                        request.Latitude.Value, request.Longitude.Value,
                        w.GeofenceLatitude!.Value, w.GeofenceLongitude!.Value) <= w.GeofenceRadiusMeters!.Value);

                    if (!withinRange)
                    {
                        return Results.Json(
                            new { message = "You must be at your assigned warehouse to sign in." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }

            var token = await tokenService.IssueTokenAsync(user, cancellationToken);

            // Same lookup PermissionService/PermissionAuthorizationHandler already use elsewhere
            // (RolePermissions rows for the user's roles, IsGranted = true) - computed once here
            // so the mobile app can decide which Home-screen cards to show without a second
            // round trip or re-implementing the permission matrix client-side.
            var permissions = await db.RolePermissions
                .Where(p => roleList.Contains(p.RoleName) && p.IsGranted)
                .Select(p => p.PermissionKey)
                .Distinct()
                .ToListAsync(cancellationToken);

            return Results.Ok(new LoginResponse(token.AccessToken, token.ExpiresAt, user.Id, user.FullName, roleList, permissions));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login")
        .WithName("Login");

        return group;
    }
}
