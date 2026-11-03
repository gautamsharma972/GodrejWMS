using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <summary>Data-only migration for the "Floor Supervisor" role: a floor-level role, reached
    /// from the Pickers mobile app, whose job is narrowly edit-location + confirm for both the
    /// Inward-without-PKM putaway queue and the Pullout (outward) pick queue - no create/upload/
    /// reject authority. Same idempotent pattern as
    /// 20261101040000_AddPickersRoleAndAssignPkmPermission: NOT EXISTS guards, clone the full
    /// RolePermissions key set from Superadmin at IsGranted = 0, then grant only the keys this
    /// role actually needs.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103100000_AddFloorSupervisorRole")]
    public partial class AddFloorSupervisorRole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                SELECT UUID(), 'Floor Supervisor', 'FLOOR SUPERVISOR', UUID()
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'FLOOR SUPERVISOR');

                INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
                SELECT 'Floor Supervisor', s.PermissionKey, 0
                FROM RolePermissions s
                WHERE s.RoleName = 'Superadmin'
                  AND NOT EXISTS (SELECT 1 FROM RolePermissions p WHERE p.RoleName = 'Floor Supervisor' AND p.PermissionKey = s.PermissionKey);

                UPDATE RolePermissions SET IsGranted = 1
                WHERE RoleName = 'Floor Supervisor'
                  AND PermissionKey IN (
                    'Menu.ViewInward', 'Menu.ViewInwardNoPkm', 'Inward.View', 'Inward.Edit', 'Inward.Confirm',
                    'Menu.ViewPullout', 'Pullout.View', 'Pullout.Edit', 'Pullout.Confirm'
                  );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM RolePermissions WHERE RoleName = 'Floor Supervisor';
                DELETE FROM AspNetRoles WHERE NormalizedName = 'FLOOR SUPERVISOR';
            ");
        }
    }
}
