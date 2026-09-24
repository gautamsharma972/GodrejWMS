using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialPreferredZoneAndPutawayOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreferredZoneTypeId",
                table: "Materials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "InwardPutaways",
                type: "varchar(250)",
                maxLength: 250,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<uint>(
                name: "RowVersion",
                table: "InwardPutaways",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_PreferredZoneTypeId",
                table: "Materials",
                column: "PreferredZoneTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_ZoneTypes_PreferredZoneTypeId",
                table: "Materials",
                column: "PreferredZoneTypeId",
                principalTable: "ZoneTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_ZoneTypes_PreferredZoneTypeId",
                table: "Materials");

            migrationBuilder.DropIndex(
                name: "IX_Materials_PreferredZoneTypeId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "PreferredZoneTypeId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "InwardPutaways");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "InwardPutaways");
        }
    }
}
