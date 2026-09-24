using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationSubtypeMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationSubtypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationSubtypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Seed the fixed subtype rows at the same Ids the retired LocationSubtype enum used
            // (1=Good, 2=Damage, 3=Expire, 4=Hold) so the FK constraints added below - against the
            // existing LocationSubtype/StockSubtype int columns - resolve without touching any data.
            migrationBuilder.InsertData(
                table: "LocationSubtypes",
                columns: new[] { "Id", "Code", "DisplayName", "Description", "SortOrder", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "Good", "Good", "Saleable stock location", 10, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 2, "Damage", "Damage", "Damaged stock location", 20, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 3, "Expire", "Expire", "Expired or near-expiry stock location", 30, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 4, "Hold", "Hold", "Quality hold location", 40, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_StockSubtype",
                table: "StockBatches",
                column: "StockSubtype");

            migrationBuilder.CreateIndex(
                name: "IX_PalletPositions_LocationSubtype",
                table: "PalletPositions",
                column: "LocationSubtype");

            migrationBuilder.CreateIndex(
                name: "IX_LocationSubtypes_Code",
                table: "LocationSubtypes",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PalletPositions_LocationSubtypes_LocationSubtype",
                table: "PalletPositions",
                column: "LocationSubtype",
                principalTable: "LocationSubtypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockBatches_LocationSubtypes_StockSubtype",
                table: "StockBatches",
                column: "StockSubtype",
                principalTable: "LocationSubtypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PalletPositions_LocationSubtypes_LocationSubtype",
                table: "PalletPositions");

            migrationBuilder.DropForeignKey(
                name: "FK_StockBatches_LocationSubtypes_StockSubtype",
                table: "StockBatches");

            migrationBuilder.DropTable(
                name: "LocationSubtypes");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_StockSubtype",
                table: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_PalletPositions_LocationSubtype",
                table: "PalletPositions");
        }
    }
}
