using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreMfgMonthAsYearMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_MaterialId",
                table: "StockBatches",
                column: "MaterialId");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId",
                table: "StockBatches");

            ConvertDateColumnToYearMonth(migrationBuilder, "StockBatches");
            ConvertDateColumnToYearMonth(migrationBuilder, "PulloutPicks");
            ConvertDateColumnToYearMonth(migrationBuilder, "InwardTransactionLines");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId",
                table: "StockBatches",
                columns: new[] { "MaterialId", "MfgMonth", "PalletPositionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId",
                table: "StockBatches");

            ConvertYearMonthColumnToDate(migrationBuilder, "StockBatches");
            ConvertYearMonthColumnToDate(migrationBuilder, "PulloutPicks");
            ConvertYearMonthColumnToDate(migrationBuilder, "InwardTransactionLines");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_MaterialId_MfgMonth_PalletPositionId",
                table: "StockBatches",
                columns: new[] { "MaterialId", "MfgMonth", "PalletPositionId" },
                unique: true);
        }

        private static void ConvertDateColumnToYearMonth(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.Sql($"ALTER TABLE `{table}` ADD COLUMN `MfgMonth_YearMonth` int NOT NULL DEFAULT 0;");
            migrationBuilder.Sql($"UPDATE `{table}` SET `MfgMonth_YearMonth` = (YEAR(`MfgMonth`) * 100) + MONTH(`MfgMonth`);");
            migrationBuilder.Sql($"ALTER TABLE `{table}` DROP COLUMN `MfgMonth`;");
            migrationBuilder.Sql($"ALTER TABLE `{table}` CHANGE `MfgMonth_YearMonth` `MfgMonth` int NOT NULL;");
        }

        private static void ConvertYearMonthColumnToDate(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.Sql($"ALTER TABLE `{table}` ADD COLUMN `MfgMonth_Date` date NOT NULL DEFAULT '2000-01-01';");
            migrationBuilder.Sql($"UPDATE `{table}` SET `MfgMonth_Date` = STR_TO_DATE(CONCAT(`MfgMonth`, '01'), '%Y%m%d');");
            migrationBuilder.Sql($"ALTER TABLE `{table}` DROP COLUMN `MfgMonth`;");
            migrationBuilder.Sql($"ALTER TABLE `{table}` CHANGE `MfgMonth_Date` `MfgMonth` date NOT NULL;");
        }
    }
}
