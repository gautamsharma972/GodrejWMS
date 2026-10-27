using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Records the removal of model-managed seed definitions from the EF snapshot. Intentionally
/// performs no database operations so existing reference, permission, organization, and
/// warehouse data remains untouched.
/// </summary>
public partial class DisableModelManagedSeeders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
