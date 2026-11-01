using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <summary>Menu.ViewInwardNoPkm was granted only to Office Staff and Pickers, so Superadmin,
    /// Platform Admin and the supervisors never saw the Inward (No PKM) menu.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261101050000_GrantInwardNoPkmMenuToSupervisoryRoles")]
    public partial class GrantInwardNoPkmMenuToSupervisoryRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE RolePermissions SET IsGranted = 1
                WHERE PermissionKey = 'Menu.ViewInwardNoPkm'
                  AND RoleName IN ('Superadmin', 'Platform Admin', 'Head Supervisor', 'Supervisor');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE RolePermissions SET IsGranted = 0
                WHERE PermissionKey = 'Menu.ViewInwardNoPkm'
                  AND RoleName IN ('Superadmin', 'Platform Admin', 'Head Supervisor', 'Supervisor');
            ");
        }
    }
}
