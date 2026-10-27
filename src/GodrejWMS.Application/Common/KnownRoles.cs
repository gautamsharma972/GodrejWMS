namespace GodrejWMS.Application.Common;

/// <summary>The five role names as the Application layer knows them - kept as literals here
/// (not a reference to Infrastructure's RoleNames constants class) because the Application layer
/// must not depend on Infrastructure. See RoleNames.cs for the canonical definitions these must
/// stay in sync with.</summary>
public static class KnownRoles
{
    public const string Superadmin = "Superadmin";
    public const string LogisticManager = "Logistic Manager";
    public const string OfficeStaff = "Office Staff";
    public const string HeadSupervisor = "Head Supervisor";
    public const string Supervisor = "Supervisor";

    /// <summary>The vendor-level role above Superadmin - deliberately NOT in <see cref="All"/>.
    /// See RoleNames.cs for the full rationale these two must stay in sync on.</summary>
    public const string PlatformAdmin = "Platform Admin";

    public static readonly string[] All = [Superadmin, LogisticManager, OfficeStaff, HeadSupervisor, Supervisor];
}
