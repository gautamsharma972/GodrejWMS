using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <summary>Data-only migration: adds the Inward.OverrideGroundFloor permission (lets a user put a "ground floor only"
    /// design type on an upper-level location by hand) for every role - granted to Superadmin, Platform Admin, Head
    /// Supervisor and Supervisor, denied to the rest. Idempotent (NOT EXISTS guard), so re-running is harmless.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103120000_GrantOverrideGroundFloorPermission")]
    public partial class GrantOverrideGroundFloorPermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO RolePermissions (RoleName, PermissionKey, IsGranted)
                SELECT r.Name, 'Inward.OverrideGroundFloor',
                       CASE WHEN r.Name IN ('Superadmin', 'Platform Admin', 'Head Supervisor', 'Supervisor') THEN 1 ELSE 0 END
                FROM AspNetRoles r
                WHERE NOT EXISTS (SELECT 1 FROM RolePermissions p WHERE p.RoleName = r.Name AND p.PermissionKey = 'Inward.OverrideGroundFloor');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM RolePermissions WHERE PermissionKey = 'Inward.OverrideGroundFloor';");
        }
    }
}
