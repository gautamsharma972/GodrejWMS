using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVarianceMismatchAndDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualMaterialId",
                table: "InwardLineVariances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualMfgMonth",
                table: "InwardLineVariances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DestinationPalletPositionId",
                table: "InwardLineVariances",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InwardLineVariances_ActualMaterialId",
                table: "InwardLineVariances",
                column: "ActualMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardLineVariances_DestinationPalletPositionId",
                table: "InwardLineVariances",
                column: "DestinationPalletPositionId");

            migrationBuilder.AddForeignKey(
                name: "FK_InwardLineVariances_Materials_ActualMaterialId",
                table: "InwardLineVariances",
                column: "ActualMaterialId",
                principalTable: "Materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InwardLineVariances_PalletPositions_DestinationPalletPositio~",
                table: "InwardLineVariances",
                column: "DestinationPalletPositionId",
                principalTable: "PalletPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InwardLineVariances_Materials_ActualMaterialId",
                table: "InwardLineVariances");

            migrationBuilder.DropForeignKey(
                name: "FK_InwardLineVariances_PalletPositions_DestinationPalletPositio~",
                table: "InwardLineVariances");

            migrationBuilder.DropIndex(
                name: "IX_InwardLineVariances_ActualMaterialId",
                table: "InwardLineVariances");

            migrationBuilder.DropIndex(
                name: "IX_InwardLineVariances_DestinationPalletPositionId",
                table: "InwardLineVariances");

            migrationBuilder.DropColumn(
                name: "ActualMaterialId",
                table: "InwardLineVariances");

            migrationBuilder.DropColumn(
                name: "ActualMfgMonth",
                table: "InwardLineVariances");

            migrationBuilder.DropColumn(
                name: "DestinationPalletPositionId",
                table: "InwardLineVariances");
        }
    }
}
