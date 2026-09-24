using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehousesAndReportAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "Racks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "PulloutTransactions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedByUserId",
                table: "PulloutTransactions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedByUserName",
                table: "PulloutTransactions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "PulloutTransactions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "InwardTransactions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserWarehouses",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWarehouses", x => new { x.UserId, x.WarehouseId });
                    table.ForeignKey(
                        name: "FK_UserWarehouses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserWarehouses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Warehouses",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[] { 1, "GCPL", true, "GCPL" });

            migrationBuilder.Sql("UPDATE PulloutTransactions SET ConfirmedAt = CreatedAt WHERE IsConfirmed = 1 AND ConfirmedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Racks_WarehouseId_Code",
                table: "Racks",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PulloutTransactions_WarehouseId_CreatedAt",
                table: "PulloutTransactions",
                columns: new[] { "WarehouseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_WarehouseId_CreatedAt",
                table: "InwardTransactions",
                columns: new[] { "WarehouseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_WarehouseId",
                table: "UserWarehouses",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InwardTransactions_Warehouses_WarehouseId",
                table: "InwardTransactions",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PulloutTransactions_Warehouses_WarehouseId",
                table: "PulloutTransactions",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Racks_Warehouses_WarehouseId",
                table: "Racks",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InwardTransactions_Warehouses_WarehouseId",
                table: "InwardTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_PulloutTransactions_Warehouses_WarehouseId",
                table: "PulloutTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Racks_Warehouses_WarehouseId",
                table: "Racks");

            migrationBuilder.DropTable(
                name: "UserWarehouses");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Racks_WarehouseId_Code",
                table: "Racks");

            migrationBuilder.DropIndex(
                name: "IX_PulloutTransactions_WarehouseId_CreatedAt",
                table: "PulloutTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InwardTransactions_WarehouseId_CreatedAt",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "Racks");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserName",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InwardTransactions");
        }
    }
}
