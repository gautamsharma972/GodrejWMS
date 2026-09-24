namespace GodrejWMS.Infrastructure.Identity;

/// <summary>
/// The three roles used across the app: Admin manages materials/racks/users, Supervisor gets
/// full visibility and reporting, Operator runs day-to-day inward/pullout on the floor.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Operator = "Operator";

    public static readonly string[] All = [Admin, Supervisor, Operator];
}
