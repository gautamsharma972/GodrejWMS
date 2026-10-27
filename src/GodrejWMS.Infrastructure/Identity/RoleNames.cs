namespace GodrejWMS.Infrastructure.Identity;

/// <summary>
/// The five roles used across the app, per the organizational hierarchy/access-control model:
/// Superadmin has organization-wide access; Logistic Manager is scoped to assigned region(s)
/// (and the warehouses within them); Office Staff, Head Supervisor, and Supervisor are each
/// scoped to a single assigned warehouse. Superadmin was formerly "Admin" and Office Staff was
/// formerly "Operator" - the AspNetRoles rows were renamed in place (same RoleId) by the
/// RenameRolesForOrgHierarchy migration, so existing user-role assignments carried over unchanged.
/// </summary>
public static class RoleNames
{
    public const string Superadmin = "Superadmin";
    public const string LogisticManager = "Logistic Manager";
    public const string OfficeStaff = "Office Staff";
    public const string HeadSupervisor = "Head Supervisor";
    public const string Supervisor = "Supervisor";

    /// <summary>The vendor-level role above Superadmin - deliberately NOT in <see cref="All"/>,
    /// which represents the client-visible, Superadmin-assignable role set. Platform Admin is a
    /// fixed, code-level role (same treatment as Superadmin itself): never shown in the Role &amp;
    /// Permission Management matrix, never assignable from the Users page, and its own accounts
    /// never appear in the client-facing user list. See KnownRoles.cs (Application layer) for the
    /// matching constant these must stay in sync with.</summary>
    public const string PlatformAdmin = "Platform Admin";

    public static readonly string[] All = [Superadmin, LogisticManager, OfficeStaff, HeadSupervisor, Supervisor];
}
