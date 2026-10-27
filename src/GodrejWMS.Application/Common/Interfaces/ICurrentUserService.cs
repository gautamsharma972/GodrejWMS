namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Exposes the identity of the caller to the Application layer without depending on ASP.NET Core.</summary>
public interface ICurrentUserService
{
    string? UserId { get; }

    string? UserName { get; }

    bool IsInRole(string role);

    /// <summary>Every role currently assigned to this user (usually exactly one, per this app's
    /// single-role-per-user model). Unlike IsInRole, which checks one candidate name, this doesn't
    /// require knowing role names up front - used wherever "every role this user actually has"
    /// needs to be looked up against data (e.g. RolePermission grants), so a role outside the
    /// fixed built-in set still works correctly.</summary>
    IReadOnlyList<string> Roles { get; }
}
