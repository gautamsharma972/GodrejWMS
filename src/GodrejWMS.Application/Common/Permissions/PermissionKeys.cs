namespace GodrejWMS.Application.Common.Permissions;

/// <summary>
/// The fixed catalog of permission keys the app understands, grouped by module per spec section 8.
/// Which keys exist is a code concern (a new permission type is a feature, not configuration); which
/// roles hold which key is entirely data-driven via <see cref="GodrejWMS.Domain.Entities.RolePermission"/>
/// and editable by Superadmin on the Role &amp; Permission Management screen without a code change.
/// </summary>
public static class PermissionKeys
{
    public static class Menu
    {
        public const string ViewInward = "Menu.ViewInward";
        public const string ViewPullout = "Menu.ViewPullout";
        public const string ViewInventoryMaster = "Menu.ViewInventoryMaster";
        public const string ViewInventoryAdjustments = "Menu.ViewInventoryAdjustments";
        public const string ViewRacks = "Menu.ViewRacks";
        public const string ViewLocations = "Menu.ViewLocations";
        public const string ViewMaterials = "Menu.ViewMaterials";
        public const string ViewInventoryMovement = "Menu.ViewInventoryMovement";
        public const string ViewAuditTrail = "Menu.ViewAuditTrail";
        public const string ViewReports = "Menu.ViewReports";
        public const string ViewOrgMasters = "Menu.ViewOrgMasters";
        public const string ViewProductMasters = "Menu.ViewProductMasters";
        public const string ViewUserManagement = "Menu.ViewUserManagement";
    }

    public static class Inward
    {
        public const string Create = "Inward.Create";
        public const string View = "Inward.View";
        public const string Edit = "Inward.Edit";
        public const string Putaway = "Inward.Putaway";
        public const string Confirm = "Inward.Confirm";
        public const string Reject = "Inward.Reject";
        public const string Reallocate = "Inward.Reallocate";
    }

    public static class Pullout
    {
        public const string Create = "Pullout.Create";
        public const string View = "Pullout.View";
        public const string Edit = "Pullout.Edit";
        public const string Pick = "Pullout.Pick";
        public const string Confirm = "Pullout.Confirm";
        public const string Reject = "Pullout.Reject";
    }

    public static class Inventory
    {
        public const string View = "Inventory.View";
        public const string Import = "Inventory.Import";
        public const string Adjust = "Inventory.Adjust";
        public const string ApproveAdjustment = "Inventory.ApproveAdjustment";
        public const string Move = "Inventory.Move";
        public const string ManageLocations = "Inventory.ManageLocations";
        public const string ManageRacks = "Inventory.ManageRacks";
    }

    public static class Masters
    {
        public const string View = "Masters.View";
        public const string Create = "Masters.Create";
        public const string Edit = "Masters.Edit";
        public const string Deactivate = "Masters.Deactivate";
        public const string Delete = "Masters.Delete";
    }

    public static class Reports
    {
        public const string View = "Reports.View";
        public const string Export = "Reports.Export";
    }

    public static class AuditTrail
    {
        public const string View = "AuditTrail.View";
    }

    public static class Users
    {
        public const string View = "Users.View";
        public const string ManageAccounts = "Users.ManageAccounts";
        public const string Edit = "Users.Edit";
        public const string Delete = "Users.Delete";
        public const string AssignRoleAndScope = "Users.AssignRoleAndScope";
        public const string ConfigurePermissions = "Users.ConfigurePermissions";
        public const string ResetPassword = "Users.ResetPassword";
    }

    /// <summary>Every key above, for seeding and for rendering the full matrix.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Menu.ViewInward, Menu.ViewPullout, Menu.ViewInventoryMaster, Menu.ViewInventoryAdjustments,
        Menu.ViewRacks, Menu.ViewLocations, Menu.ViewMaterials, Menu.ViewInventoryMovement,
        Menu.ViewAuditTrail, Menu.ViewReports, Menu.ViewOrgMasters, Menu.ViewProductMasters, Menu.ViewUserManagement,
        Inward.Create, Inward.View, Inward.Edit, Inward.Putaway, Inward.Confirm, Inward.Reject, Inward.Reallocate,
        Pullout.Create, Pullout.View, Pullout.Edit, Pullout.Pick, Pullout.Confirm, Pullout.Reject,
        Inventory.View, Inventory.Import, Inventory.Adjust, Inventory.ApproveAdjustment, Inventory.Move, Inventory.ManageLocations, Inventory.ManageRacks,
        Masters.View, Masters.Create, Masters.Edit, Masters.Deactivate, Masters.Delete,
        Reports.View, Reports.Export,
        AuditTrail.View,
        Users.View, Users.ManageAccounts, Users.Edit, Users.Delete, Users.AssignRoleAndScope, Users.ConfigurePermissions, Users.ResetPassword
    ];
}
