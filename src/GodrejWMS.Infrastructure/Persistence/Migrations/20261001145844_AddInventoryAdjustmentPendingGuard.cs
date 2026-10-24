using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAdjustmentPendingGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpenStockBatchId",
                table: "InventoryAdjustments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_OpenStockBatchId",
                table: "InventoryAdjustments",
                column: "OpenStockBatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_OpenStockBatchId",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "OpenStockBatchId",
                table: "InventoryAdjustments");
        }
    }
}
