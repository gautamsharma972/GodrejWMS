using System;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260829123000_AddInwardPutawayConfirmation")]
    public partial class AddInwardPutawayConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL 8+ refuses to drop an index that still backs a foreign key, so the
            // FK is dropped first and re-added below once the replacement (composite)
            // index that also covers PalletPositionId is in place.
            migrationBuilder.DropForeignKey(
                name: "FK_InwardPutaways_PalletPositions_PalletPositionId",
                table: "InwardPutaways");

            migrationBuilder.DropIndex(
                name: "IX_InwardPutaways_PalletPositionId",
                table: "InwardPutaways");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "InwardPutaways",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedByUserId",
                table: "InwardPutaways",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsConfirmed",
                table: "InwardPutaways",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_InwardPutaways_PalletPositionId_IsConfirmed",
                table: "InwardPutaways",
                columns: new[] { "PalletPositionId", "IsConfirmed" });

            migrationBuilder.AddForeignKey(
                name: "FK_InwardPutaways_PalletPositions_PalletPositionId",
                table: "InwardPutaways",
                column: "PalletPositionId",
                principalTable: "PalletPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InwardPutaways_PalletPositions_PalletPositionId",
                table: "InwardPutaways");

            migrationBuilder.DropIndex(
                name: "IX_InwardPutaways_PalletPositionId_IsConfirmed",
                table: "InwardPutaways");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "InwardPutaways");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "InwardPutaways");

            migrationBuilder.DropColumn(
                name: "IsConfirmed",
                table: "InwardPutaways");

            migrationBuilder.CreateIndex(
                name: "IX_InwardPutaways_PalletPositionId",
                table: "InwardPutaways",
                column: "PalletPositionId");

            migrationBuilder.AddForeignKey(
                name: "FK_InwardPutaways_PalletPositions_PalletPositionId",
                table: "InwardPutaways",
                column: "PalletPositionId",
                principalTable: "PalletPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}