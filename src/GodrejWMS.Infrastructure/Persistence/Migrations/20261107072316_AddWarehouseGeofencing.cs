using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseGeofencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GeofenceEnabled",
                table: "Warehouses",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "GeofenceLatitude",
                table: "Warehouses",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GeofenceLongitude",
                table: "Warehouses",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeofenceRadiusMeters",
                table: "Warehouses",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeofenceEnabled",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "GeofenceLatitude",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "GeofenceLongitude",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "GeofenceRadiusMeters",
                table: "Warehouses");
        }
    }
}
