using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <summary>Platform Admin is the vendor-level role, not a client role - it should only ever
    /// reach the hidden /platform-admin/* console (gated by [Authorize(Roles = "Platform Admin")],
    /// independent of RolePermissions), never any client-facing menu or page. Platform Admin was
    /// previously granted every permission a Superadmin has (so new permissions, which clone off
    /// Superadmin, would also land here) - this revokes all of them, which also empties NavMenu
    /// for this role since every item there is behind a PermissionAuthorize check. Idempotent and
    /// reversible: Down restores the previous "every permission granted" state.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103200000_RestrictPlatformAdminToVendorConsole")]
    public partial class RestrictPlatformAdminToVendorConsole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE RolePermissions SET IsGranted = 0 WHERE RoleName = 'Platform Admin';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE RolePermissions SET IsGranted = 1 WHERE RoleName = 'Platform Admin';
            ");
        }
    }
}
