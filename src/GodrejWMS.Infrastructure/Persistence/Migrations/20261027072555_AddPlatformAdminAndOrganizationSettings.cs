using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAdminAndOrganizationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: true),
                    MaxWarehouses = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "OrganizationSettings",
                columns: new[] { "Id", "MaxUsers", "MaxWarehouses" },
                values: new object[] { 1, null, null });

            // Platform Admin (vendor-only role) has no app-level seeder (seeders are intentionally
            // disabled at startup - see Program.cs), so the AspNetRoles row is created here, same
            // pattern as the Logistic Manager/Head Supervisor inserts in
            // RenameRolesForOrgHierarchy. Guarded by NOT EXISTS so re-running against an
            // environment that somehow already has it is a no-op, not a duplicate-key error.
            migrationBuilder.Sql(
                "INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) " +
                "SELECT 'f3a1c7d2-5b8e-4c6a-9d3f-7e2b4c8a1f6d', 'Platform Admin', 'PLATFORM ADMIN', UUID() " +
                "WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'Platform Admin');");

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionKey", "RoleName", "IsGranted" },
                values: new object[,]
                {
                    { "AuditTrail.View", "Platform Admin", true },
                    { "Inventory.Adjust", "Platform Admin", true },
                    { "Inventory.ApproveAdjustment", "Platform Admin", true },
                    { "Inventory.Import", "Platform Admin", true },
                    { "Inventory.ManageLocations", "Platform Admin", true },
                    { "Inventory.ManageRacks", "Platform Admin", true },
                    { "Inventory.Move", "Platform Admin", true },
                    { "Inventory.View", "Platform Admin", true },
                    { "Inward.Confirm", "Platform Admin", true },
                    { "Inward.Create", "Platform Admin", true },
                    { "Inward.Edit", "Platform Admin", true },
                    { "Inward.Putaway", "Platform Admin", true },
                    { "Inward.Reallocate", "Platform Admin", true },
                    { "Inward.Reject", "Platform Admin", true },
                    { "Inward.View", "Platform Admin", true },
                    { "Masters.Create", "Platform Admin", true },
                    { "Masters.Deactivate", "Platform Admin", true },
                    { "Masters.Delete", "Platform Admin", true },
                    { "Masters.Edit", "Platform Admin", true },
                    { "Masters.View", "Platform Admin", true },
                    { "Menu.ViewAuditTrail", "Platform Admin", true },
                    { "Menu.ViewInventoryAdjustments", "Platform Admin", true },
                    { "Menu.ViewInventoryMaster", "Platform Admin", true },
                    { "Menu.ViewInventoryMovement", "Platform Admin", true },
                    { "Menu.ViewInward", "Platform Admin", true },
                    { "Menu.ViewLocations", "Platform Admin", true },
                    { "Menu.ViewMaterials", "Platform Admin", true },
                    { "Menu.ViewOrgMasters", "Platform Admin", true },
                    { "Menu.ViewProductMasters", "Platform Admin", true },
                    { "Menu.ViewPullout", "Platform Admin", true },
                    { "Menu.ViewRacks", "Platform Admin", true },
                    { "Menu.ViewReports", "Platform Admin", true },
                    { "Menu.ViewUserManagement", "Platform Admin", true },
                    { "Pullout.Confirm", "Platform Admin", true },
                    { "Pullout.Create", "Platform Admin", true },
                    { "Pullout.Edit", "Platform Admin", true },
                    { "Pullout.Pick", "Platform Admin", true },
                    { "Pullout.Reject", "Platform Admin", true },
                    { "Pullout.View", "Platform Admin", true },
                    { "Reports.Export", "Platform Admin", true },
                    { "Reports.View", "Platform Admin", true },
                    { "Users.AssignRoleAndScope", "Platform Admin", true },
                    { "Users.ConfigurePermissions", "Platform Admin", true },
                    { "Users.ManageAccounts", "Platform Admin", true },
                    { "Users.ResetPassword", "Platform Admin", true },
                    { "Users.View", "Platform Admin", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationSettings");

            migrationBuilder.Sql("DELETE FROM AspNetRoles WHERE Name = 'Platform Admin';");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "AuditTrail.View", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.Adjust", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.ApproveAdjustment", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.Import", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.ManageLocations", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.ManageRacks", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.Move", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inventory.View", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.Confirm", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.Create", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.Edit", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.Putaway", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.Reallocate", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.Reject", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Inward.View", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.View", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewAuditTrail", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewInventoryAdjustments", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewInventoryMaster", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewInventoryMovement", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewInward", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewLocations", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewMaterials", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewOrgMasters", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewProductMasters", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewPullout", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewRacks", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewReports", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewUserManagement", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Pullout.Confirm", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Pullout.Create", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Pullout.Edit", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Pullout.Pick", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Pullout.Reject", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Pullout.View", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Reports.Export", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Reports.View", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.AssignRoleAndScope", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.ConfigurePermissions", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.ManageAccounts", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.ResetPassword", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.View", "Platform Admin" });
        }
    }
}
