using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameRolesForOrgHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renames existing AspNetRoles rows IN PLACE (same Id) so every existing
            // AspNetUserRoles assignment carries over unchanged - no user loses or gains access
            // as part of this rename. "Supervisor" is untouched. "Logistic Manager" and
            // "Head Supervisor" are brand new roles with no prior users to migrate; they are
            // created explicitly below because application startup seeders are disabled.
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name = 'Superadmin', NormalizedName = 'SUPERADMIN' WHERE Name = 'Admin';");
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name = 'Office Staff', NormalizedName = 'OFFICE STAFF' WHERE Name = 'Operator';");

            // The app's demo-data seeder (which used to create any missing roles on startup) is
            // disabled, so the two brand-new roles have to be created here instead. Guarded by
            // NOT EXISTS so re-running this migration (or applying it to an environment that
            // somehow already has these roles) is a no-op rather than a duplicate-key error.
            migrationBuilder.Sql(
                "INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) " +
                "SELECT '7b1c9e2a-3f4d-4a5b-8c6d-1e2f3a4b5c6d', 'Logistic Manager', 'LOGISTIC MANAGER', UUID() " +
                "WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'Logistic Manager');");
            migrationBuilder.Sql(
                "INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) " +
                "SELECT '9d4e8f1b-6a2c-4d3e-9b7a-2c3d4e5f6a7b', 'Head Supervisor', 'HEAD SUPERVISOR', UUID() " +
                "WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'Head Supervisor');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM AspNetRoles WHERE Name = 'Logistic Manager';");
            migrationBuilder.Sql("DELETE FROM AspNetRoles WHERE Name = 'Head Supervisor';");
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name = 'Admin', NormalizedName = 'ADMIN' WHERE Name = 'Superadmin';");
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name = 'Operator', NormalizedName = 'OPERATOR' WHERE Name = 'Office Staff';");
        }
    }
}
