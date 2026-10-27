namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Bridges the Application layer to ASP.NET Core Identity's RoleManager, the same reason
/// IUserManagementService exists - keeps role lifecycle testable/usable from MediatR handlers
/// without the Application layer depending on ASP.NET Core Identity directly.</summary>
public interface IRoleManagementService
{
    /// <summary>Every role that currently exists, ordered: Superadmin first, then the other four
    /// built-in roles in their historical fixed order, then any custom roles alphabetically - so
    /// the Role &amp; Permission Management matrix's column order never shifts for existing roles
    /// as new ones are added.</summary>
    Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken cancellationToken);

    /// <summary>Creates the role and seeds one RolePermission row (ungranted) per known permission
    /// key, so it appears in the matrix ready for Superadmin to configure. Throws if the name is
    /// already taken.</summary>
    Task CreateRoleAsync(string name, CancellationToken cancellationToken);

    /// <summary>Deletes the role and its RolePermission rows. Throws if the role has any user
    /// still assigned to it, or if it's one of the five built-in roles.</summary>
    Task DeleteRoleAsync(string name, CancellationToken cancellationToken);
}
