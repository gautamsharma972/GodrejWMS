using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEditDeletePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionKey", "RoleName", "IsGranted" },
                values: new object[,]
                {
                    { "Users.Delete", "Head Supervisor", false },
                    { "Users.Edit", "Head Supervisor", false },
                    { "Users.Delete", "Logistic Manager", false },
                    { "Users.Edit", "Logistic Manager", false },
                    { "Users.Delete", "Office Staff", false },
                    { "Users.Edit", "Office Staff", false },
                    { "Users.Delete", "Platform Admin", true },
                    { "Users.Edit", "Platform Admin", true },
                    { "Users.Delete", "Superadmin", true },
                    { "Users.Edit", "Superadmin", true },
                    { "Users.Delete", "Supervisor", false },
                    { "Users.Edit", "Supervisor", false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Delete", "Head Supervisor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Edit", "Head Supervisor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Delete", "Logistic Manager" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Edit", "Logistic Manager" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Delete", "Office Staff" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Edit", "Office Staff" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Delete", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Edit", "Platform Admin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Delete", "Superadmin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Edit", "Superadmin" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Delete", "Supervisor" });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionKey", "RoleName" },
                keyValues: new object[] { "Users.Edit", "Supervisor" });
        }
    }
}
