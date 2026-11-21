using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CycleCountBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BatchNumber = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ScheduleFrom = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ScheduleTo = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DropReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CardsGenerated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
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
                    table.PrimaryKey("PK_CycleCountBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleCountBatches_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CycleCountBatchRacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CycleCountBatchId = table.Column<int>(type: "int", nullable: false),
                    RackCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_CycleCountBatchRacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleCountBatchRacks_CycleCountBatches_CycleCountBatchId",
                        column: x => x.CycleCountBatchId,
                        principalTable: "CycleCountBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CycleCountCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CycleCountBatchId = table.Column<int>(type: "int", nullable: false),
                    RackCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PalletPositionId = table.Column<int>(type: "int", nullable: false),
                    SequenceKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SystemMaterialId = table.Column<int>(type: "int", nullable: true),
                    SystemMfgMonth = table.Column<int>(type: "int", nullable: true),
                    SystemQuantityBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    PhysicalMaterialId = table.Column<int>(type: "int", nullable: true),
                    PhysicalMfgMonth = table.Column<int>(type: "int", nullable: true),
                    PhysicalQuantityBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: true),
                    DifferenceBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    IsExtra = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsExcluded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CountedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_CycleCountCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleCountCards_CycleCountBatches_CycleCountBatchId",
                        column: x => x.CycleCountBatchId,
                        principalTable: "CycleCountBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CycleCountCards_Materials_PhysicalMaterialId",
                        column: x => x.PhysicalMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountCards_Materials_SystemMaterialId",
                        column: x => x.SystemMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CycleCountCards_PalletPositions_PalletPositionId",
                        column: x => x.PalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CycleCountHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CycleCountBatchId = table.Column<int>(type: "int", nullable: false),
                    CycleCountCardId = table.Column<int>(type: "int", nullable: true),
                    RackCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousValue = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewValue = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleCountHistory_CycleCountBatches_CycleCountBatchId",
                        column: x => x.CycleCountBatchId,
                        principalTable: "CycleCountBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CycleCountReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CycleCountBatchId = table.Column<int>(type: "int", nullable: false),
                    RackCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ReviewRemarks = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleCountReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CycleCountReviews_CycleCountBatches_CycleCountBatchId",
                        column: x => x.CycleCountBatchId,
                        principalTable: "CycleCountBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountBatches_BatchNumber",
                table: "CycleCountBatches",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountBatches_WarehouseId_Status",
                table: "CycleCountBatches",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountBatchRacks_CycleCountBatchId_RackCode",
                table: "CycleCountBatchRacks",
                columns: new[] { "CycleCountBatchId", "RackCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountCards_CycleCountBatchId_PalletPositionId",
                table: "CycleCountCards",
                columns: new[] { "CycleCountBatchId", "PalletPositionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountCards_CycleCountBatchId_RackCode_SequenceKey",
                table: "CycleCountCards",
                columns: new[] { "CycleCountBatchId", "RackCode", "SequenceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountCards_PalletPositionId",
                table: "CycleCountCards",
                column: "PalletPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountCards_PhysicalMaterialId",
                table: "CycleCountCards",
                column: "PhysicalMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountCards_SystemMaterialId",
                table: "CycleCountCards",
                column: "SystemMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountHistory_CycleCountBatchId_OccurredAt",
                table: "CycleCountHistory",
                columns: new[] { "CycleCountBatchId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountHistory_CycleCountCardId",
                table: "CycleCountHistory",
                column: "CycleCountCardId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountReviews_CycleCountBatchId_RackCode",
                table: "CycleCountReviews",
                columns: new[] { "CycleCountBatchId", "RackCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CycleCountBatchRacks");

            migrationBuilder.DropTable(
                name: "CycleCountCards");

            migrationBuilder.DropTable(
                name: "CycleCountHistory");

            migrationBuilder.DropTable(
                name: "CycleCountReviews");

            migrationBuilder.DropTable(
                name: "CycleCountBatches");
        }
    }
}
