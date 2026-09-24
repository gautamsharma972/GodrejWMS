using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSkuMovementTypeMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SkuMovementTypes",
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
                    table.PrimaryKey("PK_SkuMovementTypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Seed the fixed movement-type rows at the same Ids the retired SkuMovementType enum
            // used (1=FastMoving, 2=SlowMoving) so the FK constraint added below - against the
            // existing Materials.MovementType int column - resolves without touching any data.
            migrationBuilder.InsertData(
                table: "SkuMovementTypes",
                columns: new[] { "Id", "Code", "DisplayName", "Description", "SortOrder", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "FastMoving", "Fast-moving", "High velocity SKU", 10, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 2, "SlowMoving", "Slow-moving", "Lower velocity SKU", 20, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Materials_MovementType",
                table: "Materials",
                column: "MovementType");

            migrationBuilder.CreateIndex(
                name: "IX_SkuMovementTypes_Code",
                table: "SkuMovementTypes",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_SkuMovementTypes_MovementType",
                table: "Materials",
                column: "MovementType",
                principalTable: "SkuMovementTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_SkuMovementTypes_MovementType",
                table: "Materials");

            migrationBuilder.DropTable(
                name: "SkuMovementTypes");

            migrationBuilder.DropIndex(
                name: "IX_Materials_MovementType",
                table: "Materials");
        }
    }
}
