using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleCountOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhysicalStockSubtypeId",
                table: "CycleCountCards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SystemStockSubtypeId",
                table: "CycleCountCards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesDamageStock",
                table: "CycleCountBatchRacks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoReleaseFreezeAtScheduleEnd",
                table: "CycleCountBatches",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhysicalStockSubtypeId",
                table: "CycleCountCards");

            migrationBuilder.DropColumn(
                name: "SystemStockSubtypeId",
                table: "CycleCountCards");

            migrationBuilder.DropColumn(
                name: "IncludesDamageStock",
                table: "CycleCountBatchRacks");

            migrationBuilder.DropColumn(
                name: "AutoReleaseFreezeAtScheduleEnd",
                table: "CycleCountBatches");
        }
    }
}
