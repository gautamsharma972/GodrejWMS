using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialConversions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ConversionNumber = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    FromMaterialId = table.Column<int>(type: "int", nullable: false),
                    ToMaterialId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    ReasonText = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ReviewComment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowVersion = table.Column<uint>(type: "int unsigned", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialConversions_Materials_FromMaterialId",
                        column: x => x.FromMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConversions_Materials_ToMaterialId",
                        column: x => x.ToMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConversions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MaterialConversionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MaterialConversionId = table.Column<int>(type: "int", nullable: false),
                    StockBatchId = table.Column<int>(type: "int", nullable: true),
                    PalletPositionId = table.Column<int>(type: "int", nullable: false),
                    MfgMonth = table.Column<int>(type: "int", nullable: false),
                    StockSubtypeId = table.Column<int>(type: "int", nullable: false),
                    QuantityBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    SystemQuantityAtRequest = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    BatchRowVersionAtRequest = table.Column<uint>(type: "int unsigned", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialConversionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialConversionLines_MaterialConversions_MaterialConversi~",
                        column: x => x.MaterialConversionId,
                        principalTable: "MaterialConversions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialConversionLines_PalletPositions_PalletPositionId",
                        column: x => x.PalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialConversionLines_StockBatches_StockBatchId",
                        column: x => x.StockBatchId,
                        principalTable: "StockBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversionLines_MaterialConversionId",
                table: "MaterialConversionLines",
                column: "MaterialConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversionLines_PalletPositionId",
                table: "MaterialConversionLines",
                column: "PalletPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversionLines_StockBatchId",
                table: "MaterialConversionLines",
                column: "StockBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversions_ConversionNumber",
                table: "MaterialConversions",
                column: "ConversionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversions_FromMaterialId_ToMaterialId",
                table: "MaterialConversions",
                columns: new[] { "FromMaterialId", "ToMaterialId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversions_ToMaterialId",
                table: "MaterialConversions",
                column: "ToMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialConversions_WarehouseId_Status_RequestedAt",
                table: "MaterialConversions",
                columns: new[] { "WarehouseId", "Status", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialConversionLines");

            migrationBuilder.DropTable(
                name: "MaterialConversions");
        }
    }
}
