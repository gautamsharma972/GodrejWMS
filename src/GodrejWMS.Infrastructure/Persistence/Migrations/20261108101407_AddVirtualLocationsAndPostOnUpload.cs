using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtualLocationsAndPostOnUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Overflow zone owns every virtual location (see PalletPosition.IsVirtual). Idempotent.
            // Id is a fixed, far-out-of-range value (ZoneTypeIds.Overflow), not the next sequential
            // one - a warehouse admin may already have created custom zone types through the UI
            // before this migration ever runs, so the next auto-increment ID is not guaranteed free.
            migrationBuilder.Sql(@"
                INSERT INTO ZoneTypes (Id, Code, DisplayName, Description, SortOrder, IsActive, CreatedAt)
                SELECT 9000, 'Overflow', 'Overflow', 'Auto-generated virtual overflow locations', 90, 1, UTC_TIMESTAMP(6)
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM ZoneTypes WHERE Code = 'Overflow');

                UPDATE RolePermissions SET IsGranted = 1
                WHERE (RoleName = 'Pickers' AND PermissionKey IN ('Menu.ViewPullout', 'Pullout.View', 'Pullout.Pick'))
                   OR (RoleName = 'Floor Supervisor' AND PermissionKey = 'Pullout.Pick');
            ");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Racks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UploadHash",
                table: "PulloutTransactions",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PickedAt",
                table: "PulloutPicks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickedByUserId",
                table: "PulloutPicks",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PickedByUserName",
                table: "PulloutPicks",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "PickedQuantityBoxes",
                table: "PulloutPicks",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVirtual",
                table: "PalletPositions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReleasedAt",
                table: "PalletPositions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VirtualStatus",
                table: "PalletPositions",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PostedAt",
                table: "InwardTransactions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadHash",
                table: "InwardTransactions",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StockLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PostingNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntryType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    MfgMonth = table.Column<int>(type: "int", nullable: false),
                    StockSubtypeId = table.Column<int>(type: "int", nullable: false),
                    PalletPositionId = table.Column<int>(type: "int", nullable: false),
                    QuantityBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    BalanceAfterBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    ReferenceType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferenceNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedgerEntries_PalletPositions_PalletPositionId",
                        column: x => x.PalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VirtualLocationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PalletPositionId = table.Column<int>(type: "int", nullable: false),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: true),
                    InwardPutawayId = table.Column<int>(type: "int", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualLocationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VirtualLocationLogs_PalletPositions_PalletPositionId",
                        column: x => x.PalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VirtualLocationSequences",
                columns: table => new
                {
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualLocationSequences", x => x.WarehouseId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PulloutTransactions_WarehouseId_UploadHash",
                table: "PulloutTransactions",
                columns: new[] { "WarehouseId", "UploadHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PalletPositions_IsVirtual_VirtualStatus",
                table: "PalletPositions",
                columns: new[] { "IsVirtual", "VirtualStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_WarehouseId_UploadHash",
                table: "InwardTransactions",
                columns: new[] { "WarehouseId", "UploadHash" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_MaterialId_CreatedAt",
                table: "StockLedgerEntries",
                columns: new[] { "MaterialId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_PalletPositionId_CreatedAt",
                table: "StockLedgerEntries",
                columns: new[] { "PalletPositionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_PostingNumber",
                table: "StockLedgerEntries",
                column: "PostingNumber");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_ReferenceType_ReferenceNumber",
                table: "StockLedgerEntries",
                columns: new[] { "ReferenceType", "ReferenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_WarehouseId_CreatedAt",
                table: "StockLedgerEntries",
                columns: new[] { "WarehouseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualLocationLogs_InwardTransactionId",
                table: "VirtualLocationLogs",
                column: "InwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualLocationLogs_PalletPositionId_ReleasedAt",
                table: "VirtualLocationLogs",
                columns: new[] { "PalletPositionId", "ReleasedAt" });

            // After the schema changes above (they need the IsSystem column and the sequence table).
            migrationBuilder.Sql(@"
                -- Every warehouse gets its hidden overflow rack and number counter up front, so two first-ever
                -- overflow placements racing each other have nothing to create (and cannot collide on it).
                INSERT INTO Racks (Code, Name, Columns, FromColumn, Levels, ShelfLengthMm, ShelfWidthMm, ShelfHeightMm,
                                   StartingDistancePriority, IsActive, IsSystem, WarehouseId, CreatedAt)
                SELECT CONCAT('OV', w.Id), 'Overflow (virtual locations)', 0, 1, 1, 0, 0, 0, 1, 1, 1, w.Id, UTC_TIMESTAMP(6)
                FROM Warehouses w
                WHERE NOT EXISTS (SELECT 1 FROM Racks r WHERE r.WarehouseId = w.Id AND r.IsSystem = 1);

                INSERT INTO VirtualLocationSequences (WarehouseId, LastValue)
                SELECT w.Id, 0 FROM Warehouses w
                WHERE NOT EXISTS (SELECT 1 FROM VirtualLocationSequences s WHERE s.WarehouseId = w.Id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE RolePermissions SET IsGranted = 0
                WHERE (RoleName = 'Pickers' AND PermissionKey IN ('Menu.ViewPullout', 'Pullout.View', 'Pullout.Pick'))
                   OR (RoleName = 'Floor Supervisor' AND PermissionKey = 'Pullout.Pick');
                -- The ledger and the virtual-location log go with their tables. Virtual locations that nothing refers to are
                -- removed; any that still hold stock or history stay behind as ordinary locations, so no stock is ever lost.
                DELETE FROM StockLedgerEntries;
                DELETE FROM VirtualLocationLogs;
                DELETE p FROM PalletPositions p
                WHERE p.IsVirtual = 1
                  AND NOT EXISTS (SELECT 1 FROM StockBatches x WHERE x.PalletPositionId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM InwardPutaways x WHERE x.PalletPositionId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM PulloutPicks x WHERE x.PalletPositionId = p.Id)
                  AND NOT EXISTS (SELECT 1 FROM StockMovements x WHERE x.SourcePalletPositionId = p.Id OR x.DestinationPalletPositionId = p.Id);
                DELETE FROM Racks WHERE IsSystem = 1 AND NOT EXISTS (SELECT 1 FROM PalletPositions x WHERE x.RackId = Racks.Id);
                DELETE FROM ZoneTypes WHERE Code = 'Overflow' AND NOT EXISTS (SELECT 1 FROM PalletPositions x WHERE x.ZoneType = ZoneTypes.Id);
            ");

            migrationBuilder.DropTable(
                name: "StockLedgerEntries");

            migrationBuilder.DropTable(
                name: "VirtualLocationLogs");

            migrationBuilder.DropTable(
                name: "VirtualLocationSequences");

            migrationBuilder.DropIndex(
                name: "IX_PulloutTransactions_WarehouseId_UploadHash",
                table: "PulloutTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PalletPositions_IsVirtual_VirtualStatus",
                table: "PalletPositions");

            migrationBuilder.DropIndex(
                name: "IX_InwardTransactions_WarehouseId_UploadHash",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Racks");

            migrationBuilder.DropColumn(
                name: "UploadHash",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "PickedAt",
                table: "PulloutPicks");

            migrationBuilder.DropColumn(
                name: "PickedByUserId",
                table: "PulloutPicks");

            migrationBuilder.DropColumn(
                name: "PickedByUserName",
                table: "PulloutPicks");

            migrationBuilder.DropColumn(
                name: "PickedQuantityBoxes",
                table: "PulloutPicks");

            migrationBuilder.DropColumn(
                name: "IsVirtual",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "LastReleasedAt",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "VirtualStatus",
                table: "PalletPositions");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "UploadHash",
                table: "InwardTransactions");
        }
    }
}
