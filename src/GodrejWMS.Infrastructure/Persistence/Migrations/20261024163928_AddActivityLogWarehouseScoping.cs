using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogWarehouseScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "ActivityLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_WarehouseId",
                table: "ActivityLogs",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_WarehouseId",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "ActivityLogs");
        }
    }
}
