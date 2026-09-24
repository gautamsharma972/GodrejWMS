using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationTypeMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationTypes",
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
                    table.PrimaryKey("PK_LocationTypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PalletPositions_LocationType",
                table: "PalletPositions",
                column: "LocationType");

            migrationBuilder.CreateIndex(
                name: "IX_LocationTypes_Code",
                table: "LocationTypes",
                column: "Code",
                unique: true);

            // Seed the fixed location-type rows at the same Ids the retired LocationType enum
            // used (1=Rack, 2=Pallet, 3=Floor, 4=Yard) so the FK constraints added below - against
            // the existing PalletPositions.LocationType and LocationTypeConsolidationLevels.LocationType
            // int columns - resolve without touching any data.
            migrationBuilder.InsertData(
                table: "LocationTypes",
                columns: new[] { "Id", "Code", "DisplayName", "Description", "SortOrder", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "Rack", "Rack", "Rack storage location", 10, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 2, "Pallet", "Pallet", "Standalone pallet storage location", 20, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 3, "Floor", "Floor", "Floor storage location", 30, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 4, "Yard", "Yard", "Yard storage location", 40, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                });

            migrationBuilder.AddForeignKey(
                name: "FK_LocationTypeConsolidationLevels_LocationTypes_LocationType",
                table: "LocationTypeConsolidationLevels",
                column: "LocationType",
                principalTable: "LocationTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PalletPositions_LocationTypes_LocationType",
                table: "PalletPositions",
                column: "LocationType",
                principalTable: "LocationTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocationTypeConsolidationLevels_LocationTypes_LocationType",
                table: "LocationTypeConsolidationLevels");

            migrationBuilder.DropForeignKey(
                name: "FK_PalletPositions_LocationTypes_LocationType",
                table: "PalletPositions");

            migrationBuilder.DropTable(
                name: "LocationTypes");

            migrationBuilder.DropIndex(
                name: "IX_PalletPositions_LocationType",
                table: "PalletPositions");
        }
    }
}
