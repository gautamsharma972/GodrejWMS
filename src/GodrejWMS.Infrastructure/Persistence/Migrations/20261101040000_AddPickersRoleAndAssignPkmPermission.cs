using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <summary>Data-only migration for the three-person "Inward without PKM" workflow: Office Staff
    /// uploads, a Picker assigns PKM per location, a Supervisor / Head Supervisor confirms.
    /// Adds the Inward.AssignPkm key, creates the "Pickers" role, and moves confirm authority off
    /// Office Staff. Idempotent (NOT EXISTS guards), so re-running it is harmless.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101040000_AddPickersRoleAndAssignPkmPermission")]
    public partial class AddPickersRoleAndAssignPkmPermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                SELECT UUID(), 'Pickers', 'PICKERS', UUID()
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'PICKERS');

                INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
                SELECT r.Name, 'Inward.AssignPkm', CASE WHEN r.Name IN ('Superadmin', 'Platform Admin') THEN 1 ELSE 0 END
                FROM AspNetRoles r
                WHERE NOT EXISTS (SELECT 1 FROM RolePermissions p WHERE p.RoleName = r.Name AND p.PermissionKey = 'Inward.AssignPkm');

                INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
                SELECT 'Pickers', s.PermissionKey, 0
                FROM RolePermissions s
                WHERE s.RoleName = 'Superadmin'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions p WHERE p.RoleName = 'Pickers' AND p.PermissionKey = s.PermissionKey);

                UPDATE RolePermissions SET IsGranted = 1
                WHERE (RoleName = 'Pickers' AND PermissionKey IN ('Menu.ViewInwardNoPkm', 'Inward.View', 'Inward.AssignPkm'))
                   OR (RoleName = 'Office Staff' AND PermissionKey = 'Menu.ViewInwardNoPkm');

                UPDATE RolePermissions SET IsGranted = 0
                WHERE RoleName = 'Office Staff' AND PermissionKey = 'Inward.Confirm';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE RolePermissions SET IsGranted = 1 WHERE RoleName = 'Office Staff' AND PermissionKey = 'Inward.Confirm';
                UPDATE RolePermissions SET IsGranted = 0 WHERE RoleName = 'Office Staff' AND PermissionKey = 'Menu.ViewInwardNoPkm';
                DELETE FROM RolePermissions WHERE RoleName = 'Pickers' OR PermissionKey = 'Inward.AssignPkm';
                DELETE FROM AspNetRoles WHERE NormalizedName = 'PICKERS';
            ");
        }
    }
}
