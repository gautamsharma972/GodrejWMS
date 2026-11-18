using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInwardLineVariances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId",
                table: "StockBatches");

            migrationBuilder.CreateTable(
                name: "InwardLineVariances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    InwardTransactionLineId = table.Column<int>(type: "int", nullable: true),
                    InwardPutawayId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    MfgMonth = table.Column<int>(type: "int", nullable: false),
                    PalletPositionId = table.Column<int>(type: "int", nullable: false),
                    QuantityBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    Remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InwardLineVariances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InwardLineVariances_InwardTransactionLines_InwardTransaction~",
                        column: x => x.InwardTransactionLineId,
                        principalTable: "InwardTransactionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InwardLineVariances_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InwardLineVariances_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InwardLineVariances_PalletPositions_PalletPositionId",
                        column: x => x.PalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId_StockSubty~",
                table: "StockBatches",
                columns: new[] { "MaterialId", "MfgMonth", "PalletPositionId", "StockSubtype" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InwardLineVariances_InwardTransactionId_Type",
                table: "InwardLineVariances",
                columns: new[] { "InwardTransactionId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_InwardLineVariances_InwardTransactionLineId",
                table: "InwardLineVariances",
                column: "InwardTransactionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardLineVariances_MaterialId",
                table: "InwardLineVariances",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardLineVariances_PalletPositionId",
                table: "InwardLineVariances",
                column: "PalletPositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InwardLineVariances");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId_StockSubty~",
                table: "StockBatches");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId",
                table: "StockBatches",
                columns: new[] { "MaterialId", "MfgMonth", "PalletPositionId" },
                unique: true);
        }
    }
}
