using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260824175028_AddMasterDataLocationAndPutawayStrategy")]
    public partial class AddMasterDataLocationAndPutawayStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockSubtype",
                table: "StockBatches",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CapacityBoxes",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 80,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "BoxesPerPallet",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "DistancePriority",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "LocationSubtype",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LocationType",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MaxPallets",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "ZoneType",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "MovementType",
                table: "Materials",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "PalletWeightKg",
                table: "Materials",
                type: "decimal(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE PalletPositions SET MaxPallets = 2, BoxesPerPallet = 40, CapacityBoxes = 80 WHERE CapacityBoxes < 80");
            migrationBuilder.Sql("UPDATE Materials SET PalletWeightKg = GrossWeightKg * PackSize * PalletCapacityBoxes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockSubtype",
                table: "StockBatches");

            migrationBuilder.DropColumn(
                name: "BoxesPerPallet",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "DistancePriority",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "LocationSubtype",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "LocationType",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "MaxPallets",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "ZoneType",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "MovementType",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "PalletWeightKg",
                table: "Materials");

            migrationBuilder.AlterColumn<int>(
                name: "CapacityBoxes",
                table: "PalletPositions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 80);
        }
    }
}
