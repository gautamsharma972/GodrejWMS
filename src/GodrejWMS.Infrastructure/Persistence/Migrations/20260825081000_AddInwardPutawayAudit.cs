using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260825081000_AddInwardPutawayAudit")]
    public partial class AddInwardPutawayAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InwardPutaways",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionLineId = table.Column<int>(type: "int", nullable: false),
                    PalletPositionId = table.Column<int>(type: "int", nullable: false),
                    QuantityBoxes = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InwardPutaways", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InwardPutaways_InwardTransactionLines_InwardTransactionLi~",
                        column: x => x.InwardTransactionLineId,
                        principalTable: "InwardTransactionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InwardPutaways_PalletPositions_PalletPositionId",
                        column: x => x.PalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InwardPutaways_InwardTransactionLineId",
                table: "InwardPutaways",
                column: "InwardTransactionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardPutaways_PalletPositionId",
                table: "InwardPutaways",
                column: "PalletPositionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InwardPutaways");
        }
    }
}
