using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPulloutPickPendingMove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MoveQuantityBoxes",
                table: "PulloutPicks",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoveToPalletPositionId",
                table: "PulloutPicks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PulloutPicks_MoveToPalletPositionId",
                table: "PulloutPicks",
                column: "MoveToPalletPositionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PulloutPicks_PalletPositions_MoveToPalletPositionId",
                table: "PulloutPicks",
                column: "MoveToPalletPositionId",
                principalTable: "PalletPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PulloutPicks_PalletPositions_MoveToPalletPositionId",
                table: "PulloutPicks");

            migrationBuilder.DropIndex(
                name: "IX_PulloutPicks_MoveToPalletPositionId",
                table: "PulloutPicks");

            migrationBuilder.DropColumn(
                name: "MoveQuantityBoxes",
                table: "PulloutPicks");

            migrationBuilder.DropColumn(
                name: "MoveToPalletPositionId",
                table: "PulloutPicks");
        }
    }
}
