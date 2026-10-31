using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicalMaterialToInventoryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualPhysicalQuantityBeforePosting",
                table: "InventoryAdjustmentLines",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "PhysicalBatchRowVersionAtCount",
                table: "InventoryAdjustmentLines",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhysicalMaterialId",
                table: "InventoryAdjustmentLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhysicalStockBatchIdAtCount",
                table: "InventoryAdjustmentLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PhysicalSystemQuantityAtCount",
                table: "InventoryAdjustmentLines",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLines_PhysicalMaterialId",
                table: "InventoryAdjustmentLines",
                column: "PhysicalMaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustmentLines_Materials_PhysicalMaterialId",
                table: "InventoryAdjustmentLines",
                column: "PhysicalMaterialId",
                principalTable: "Materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustmentLines_Materials_PhysicalMaterialId",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustmentLines_PhysicalMaterialId",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "ActualPhysicalQuantityBeforePosting",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "PhysicalBatchRowVersionAtCount",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "PhysicalMaterialId",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "PhysicalStockBatchIdAtCount",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "PhysicalSystemQuantityAtCount",
                table: "InventoryAdjustmentLines");
        }
    }
}
