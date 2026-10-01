using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePalletPositionBoxesPerPalletAndCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoxesPerPallet",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "CapacityBoxes",
                table: "PalletPositions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoxesPerPallet",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "CapacityBoxes",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 80);
        }
    }
}
