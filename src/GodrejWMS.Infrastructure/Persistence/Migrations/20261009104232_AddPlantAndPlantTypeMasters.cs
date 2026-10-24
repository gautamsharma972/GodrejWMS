using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantAndPlantTypeMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlantName",
                table: "PulloutTransactionLines",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PlantTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
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
                    table.PrimaryKey("PK_PlantTypes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PlantTypes",
                columns: new[] { "Id", "Code", "Name", "SortOrder", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "CFA", "CFA", 10, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 2, "RWH", "RWH", 20, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { 3, "Plant", "plant", 30, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                });

            migrationBuilder.CreateTable(
                name: "Plants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlantTypeId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_Plants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plants_PlantTypes_PlantTypeId",
                        column: x => x.PlantTypeId,
                        principalTable: "PlantTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Only Purna and Pune have a Type in the source list; every other plant's Type was left
            // blank there and is seeded as null (PlantTypeId) here rather than guessed.
            migrationBuilder.InsertData(
                table: "Plants",
                columns: new[] { "Code", "Name", "PlantTypeId", "Description", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { "C042", "Purna", 2, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C043", "Pune", 1, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C044", "N-Pur", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C046", "Indore", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C047", "Raipur", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C048", "A-bad", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C121", "Purna RWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C021", "Kolkata", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C022", "Siliguri", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C023", "Cuttack", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C024", "Patna", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C025", "Ranchi", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C027", "G-hati", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C111", "Kol-RWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C113", "G-hati-PWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C001", "Delhi", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C015", "Kundli", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C002", "G-bad", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C014", "Lucknow", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C004", "Varanasi", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C007", "Jaipur", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C008", "Z-Pur", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C010", "Jammu", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C102", "Z-Pur PWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C071", "Chennai", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C085", "Trichy", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C073", "Salem", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C083", "Panchaw", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C075", "Cochin", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C076", "Bangalore", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C077", "Hubli", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C078", "H-bad", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C079", "Vijayawada", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C131", "Panchaw-RWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C132", "H-bad RWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    { "C133", "Pondy-PWH", null, null, true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plants_Code",
                table: "Plants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plants_PlantTypeId",
                table: "Plants",
                column: "PlantTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantTypes_Code",
                table: "PlantTypes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Plants");

            migrationBuilder.DropTable(
                name: "PlantTypes");

            migrationBuilder.DropColumn(
                name: "PlantName",
                table: "PulloutTransactionLines");
        }
    }
}
