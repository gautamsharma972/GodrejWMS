using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPulloutLoadingSheetHeaderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateText",
                table: "PulloutTransactions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DriverNumber",
                table: "PulloutTransactions",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EndTime",
                table: "PulloutTransactions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "GateNumber",
                table: "PulloutTransactions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PlanTime",
                table: "PulloutTransactions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StartTime",
                table: "PulloutTransactions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TransportName",
                table: "PulloutTransactions",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber",
                table: "PulloutTransactions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VolumeText",
                table: "PulloutTransactions",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WeightText",
                table: "PulloutTransactions",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateText",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "DriverNumber",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "GateNumber",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "PlanTime",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "TransportName",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "VehicleNumber",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "VolumeText",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "WeightText",
                table: "PulloutTransactions");
        }
    }
}
