namespace GodrejWMS.Application.Common.Interfaces;

public sealed record UserAccountDto(string UserId, string Email, string FullName, bool IsActive, IReadOnlyList<string> Roles);

/// <summary>Bridges the Application layer to ASP.NET Core Identity (UserManager/RoleManager),
/// which lives in Infrastructure - the same reason IExcelService exists as an interface here with
/// its real implementation there. Keeps user/role management testable and usable from MediatR
/// handlers without the Application layer depending on ASP.NET Core Identity directly.</summary>
public interface IUserManagementService
{
    /// <summary>Client-facing listing - excludes Platform Admin (vendor-only) accounts
    /// unconditionally. Used by the Users page every role, including Superadmin, sees.</summary>
    Task<IReadOnlyList<UserAccountDto>> ListUsersAsync(CancellationToken cancellationToken);

    /// <summary>Unfiltered listing, including Platform Admin accounts - only for the hidden
    /// Platform Admin screen, never the client-facing Users page.</summary>
    Task<IReadOnlyList<UserAccountDto>> ListAllUsersAsync(CancellationToken cancellationToken);

    Task<UserAccountDto?> GetUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Creates the account and assigns the given role. Returns the new user's Id and a
    /// one-time generated temporary password (never stored anywhere after this call returns -
    /// the caller is responsible for handing it to the new user through a secure channel).</summary>
    Task<(string UserId, string TemporaryPassword)> CreateUserAsync(
        string email, string fullName, string role, CancellationToken cancellationToken);

    Task SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken);

    Task UpdateUserAsync(string userId, string email, string fullName, CancellationToken cancellationToken);

    Task DeleteUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Replaces the user's role assignment (removes every other known role, adds this
    /// one) - a user has exactly one of the five roles at a time in this app.</summary>
    Task SetRoleAsync(string userId, string role, CancellationToken cancellationToken);

    /// <summary>Generates and sets a new random password for the user, bypassing the old one (no
    /// "forgot password" token/email round trip). Returns the one-time temporary password - never
    /// stored anywhere after this call returns.</summary>
    Task<string> ResetPasswordAsync(string userId, CancellationToken cancellationToken);
}
