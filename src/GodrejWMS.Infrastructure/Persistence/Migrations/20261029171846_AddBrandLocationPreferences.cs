using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandLocationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandLocationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BrandId = table.Column<int>(type: "int", nullable: false),
                    FromPalletPositionId = table.Column<int>(type: "int", nullable: false),
                    ToPalletPositionId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_BrandLocationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandLocationPreferences_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandLocationPreferences_PalletPositions_FromPalletPositionId",
                        column: x => x.FromPalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandLocationPreferences_PalletPositions_ToPalletPositionId",
                        column: x => x.ToPalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BrandLocationPreferences_BrandId_FromPalletPositionId_ToPall~",
                table: "BrandLocationPreferences",
                columns: new[] { "BrandId", "FromPalletPositionId", "ToPalletPositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrandLocationPreferences_FromPalletPositionId",
                table: "BrandLocationPreferences",
                column: "FromPalletPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandLocationPreferences_ToPalletPositionId",
                table: "BrandLocationPreferences",
                column: "ToPalletPositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandLocationPreferences");
        }
    }
}
