using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPulloutClientIndentNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientIndentNumber",
                table: "PulloutTransactions",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PulloutTransactions_ClientIndentNumber",
                table: "PulloutTransactions",
                column: "ClientIndentNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PulloutTransactions_ClientIndentNumber",
                table: "PulloutTransactions");

            migrationBuilder.DropColumn(
                name: "ClientIndentNumber",
                table: "PulloutTransactions");
        }
    }
}
