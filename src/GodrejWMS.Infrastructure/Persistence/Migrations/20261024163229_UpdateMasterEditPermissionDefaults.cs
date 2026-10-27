using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMasterEditPermissionDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Head Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Head Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Head Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Head Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Office Staff" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Office Staff" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Office Staff" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Office Staff" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Supervisor" },
                column: "IsGranted",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Supervisor" },
                column: "IsGranted",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Head Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Head Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Head Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Head Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Office Staff" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Office Staff" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Office Staff" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Office Staff" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Create", "Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Deactivate", "Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Delete", "Supervisor" },
                column: "IsGranted",
                value: false);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Masters.Edit", "Supervisor" },
                column: "IsGranted",
                value: false);
        }
    }
}
