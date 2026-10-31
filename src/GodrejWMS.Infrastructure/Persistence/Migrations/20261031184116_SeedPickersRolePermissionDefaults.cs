using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedPickersRolePermissionDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Menu.ViewDashboard and Menu.ViewInwardNoPkm are brand-new permission keys - no
            // RolePermission row exists for them on any role yet (new roles get every key seeded
            // by RoleManagementService.CreateRoleAsync at creation time, but existing roles only
            // have rows for the keys that existed when they were created). Insert one row per
            // existing role for each new key so the Role & Permission Management screen can show
            // and toggle them, same as every other key. ViewDashboard defaults to granted for the
            // 5 built-in roles plus Platform Admin (everyone who could already reach the dashboard
            // before this lockdown); ViewInwardNoPkm defaults to ungranted for everyone (it's
            // granted explicitly to the "Pickers" role afterward, via the admin UI).
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
                SELECT r.Name, 'Menu.ViewDashboard',
                    CASE WHEN r.Name IN ('Superadmin', 'Platform Admin', 'Logistic Manager', 'Office Staff', 'Head Supervisor', 'Supervisor') THEN 1 ELSE 0 END
                FROM AspNetRoles r
                WHERE NOT EXISTS (
                    SELECT 1 FROM RolePermissions p WHERE p.RoleName = r.Name AND p.PermissionKey = 'Menu.ViewDashboard');

                INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
                SELECT r.Name, 'Menu.ViewInwardNoPkm', 0
                FROM AspNetRoles r
                WHERE NOT EXISTS (
                    SELECT 1 FROM RolePermissions p WHERE p.RoleName = r.Name AND p.PermissionKey = 'Menu.ViewInwardNoPkm');
            ");

            // ViewInventoryMaster/ViewInventoryAdjustments/ViewRacks/ViewLocations/ViewMaterials
            // are already granted to all 5 built-in roles today (confirmed against the live DB) -
            // nothing to fix there. ViewOrgMasters/ViewProductMasters are currently granted only
            // to Superadmin/Logistic Manager/Platform Admin; Supervisor/Office Staff/Head
            // Supervisor can reach those pages today only because they were never gated, so flip
            // these on for those 3 roles to preserve their current access once NavMenu/page-level
            // enforcement goes live.
            foreach (var permissionKey in new[] { "Menu.ViewOrgMasters", "Menu.ViewProductMasters" })
            {
                foreach (var roleName in new[] { "Supervisor", "Office Staff", "Head Supervisor" })
                {
                    migrationBuilder.UpdateData(
                        table: "RolePermissions",
                        keyColumns: new[] { "PermissionKey", "RoleName" },
                        keyValues: new object[] { permissionKey, roleName },
                        column: "IsGranted",
                        value: true);
                }
            }

            // Live-DB check (unlike the other 4 roles above) found Logistic Manager's
            // Menu.ViewOrgMasters already 0, not 1 as the rest of this role's masters access would
            // suggest - same "never gated, so it never mattered" situation as the 3 roles above.
            // Fix it the same way so Logistic Manager doesn't lose Organization-page access either.
            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewOrgMasters", "Logistic Manager" },
                column: "IsGranted",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM RolePermissions WHERE PermissionKey IN ('Menu.ViewDashboard', 'Menu.ViewInwardNoPkm');
            ");

            foreach (var permissionKey in new[] { "Menu.ViewOrgMasters", "Menu.ViewProductMasters" })
            {
                foreach (var roleName in new[] { "Supervisor", "Office Staff", "Head Supervisor" })
                {
                    migrationBuilder.UpdateData(
                        table: "RolePermissions",
                        keyColumns: new[] { "PermissionKey", "RoleName" },
                        keyValues: new object[] { permissionKey, roleName },
                        column: "IsGranted",
                        value: false);
                }
            }

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Menu.ViewOrgMasters", "Logistic Manager" },
                column: "IsGranted",
                value: false);
        }
    }
}
