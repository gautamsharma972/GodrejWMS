using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Common;

/// <summary>The "ground floor only" put-away restriction a Design Type can carry
/// (<see cref="DesignType.RequiresGroundFloor"/>). Materials only reference their design by code
/// (<see cref="Material.DesignType"/>), so the restriction is looked up by that code. Ground floor is level 1 of a
/// rack - the "-01" at the end of a location code such as A-02-01.</summary>
public static class GroundFloorRule
{
    public const int GroundLevel = 1;

    public static bool IsGround(PalletPosition position) => position.Level == GroundLevel;

    /// <summary>True when the material's (active) design type is restricted to ground-floor locations.</summary>
    public static async Task<bool> IsRestrictedAsync(IApplicationDbContext db, string? designType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(designType))
        {
            return false;
        }

        return await db.DesignTypes.AsNoTracking()
            .AnyAsync(d => d.Code == designType && d.IsActive && d.RequiresGroundFloor, cancellationToken);
    }

    /// <summary>Guards a manual put-away change: a restricted design type may only be moved to a ground-floor
    /// location, unless the user holds <see cref="PermissionKeys.Inward.OverrideGroundFloor"/> (supervisors), in which
    /// case the reason the change already requires is the audit trail. Throws with a clear message otherwise.</summary>
    public static async Task EnsureAllowedAsync(
        IApplicationDbContext db, IPermissionService? permissions, string? designType, PalletPosition position,
        CancellationToken cancellationToken, string? rowPrefix = null)
    {
        if (IsGround(position) || !await IsRestrictedAsync(db, designType, cancellationToken))
        {
            return;
        }

        if (await CanOverrideAsync(permissions, cancellationToken))
        {
            return;
        }

        throw new InvalidOperationException(rowPrefix + NotGroundMessage(position.LocationCode, designType!)
            + " A supervisor can override this.");
    }

    public static async Task<bool> CanOverrideAsync(IPermissionService? permissions, CancellationToken cancellationToken) =>
        permissions is not null && await permissions.HasPermissionAsync(PermissionKeys.Inward.OverrideGroundFloor, cancellationToken);

    public static string NotGroundMessage(string locationCode, string designType) =>
        $"Location {locationCode} is not a ground-floor location. Design type {designType} must be put away on ground-floor (level 01) locations.";

    public static string NoEmptyGroundMessage(string designType, decimal remainingBoxes, string? detail = null) =>
        $"Ground-floor locations are not empty - {remainingBoxes:0.###} box(es) could not be reserved. " +
        $"Design type {designType} can only be put away on ground-floor (level 01) locations." +
        (string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail);
}
