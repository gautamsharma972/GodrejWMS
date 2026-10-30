using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandAndAopDescriptionMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AopDescriptionId",
                table: "Materials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "Materials",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AopDescriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AopDescriptions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_AopDescriptionId",
                table: "Materials",
                column: "AopDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_BrandId",
                table: "Materials",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_AopDescriptions_Code",
                table: "AopDescriptions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Code",
                table: "Brands",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_AopDescriptions_AopDescriptionId",
                table: "Materials",
                column: "AopDescriptionId",
                principalTable: "AopDescriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_Brands_BrandId",
                table: "Materials",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Client-supplied master data and material mapping. This is migration data, not a runtime seeder.
            migrationBuilder.Sql("""
                INSERT INTO Brands (Code, Description, IsActive, CreatedAt)
                VALUES
                ('GK', 'GK', TRUE, '2026-10-29 00:00:00.000000'),
                ('Magic', 'Magic', TRUE, '2026-10-29 00:00:00.000000'),
                ('Hit', 'Hit', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER', 'AER', TRUE, '2026-10-29 00:00:00.000000'),
                ('Cinthol', 'Cinthol', TRUE, '2026-10-29 00:00:00.000000'),
                ('No1', 'No1', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Selfie', 'Godrej Selfie', TRUE, '2026-10-29 00:00:00.000000'),
                ('KamaSutra', 'KamaSutra', TRUE, '2026-10-29 00:00:00.000000'),
                ('Park Avenue', 'Park Avenue', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Expert', 'Godrej Expert', TRUE, '2026-10-29 00:00:00.000000'),
                ('Genteel', 'Genteel', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Professional', 'Godrej Professional', TRUE, '2026-10-29 00:00:00.000000'),
                ('Fresh Glow', 'Fresh Glow', TRUE, '2026-10-29 00:00:00.000000'),
                ('Cin Male Grmg Shaving cream', 'Cin Male Grmg Shaving cream', TRUE, '2026-10-29 00:00:00.000000'),
                ('Ezee', 'Ezee', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Fab', 'Godrej Fab', TRUE, '2026-10-29 00:00:00.000000'),
                ('SPIC', 'SPIC', TRUE, '2026-10-29 00:00:00.000000'),
                ('Protekt', 'Protekt', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Shaving Cream', 'Godrej Shaving Cream', TRUE, '2026-10-29 00:00:00.000000'),
                ('Stella', 'Stella', TRUE, '2026-10-29 00:00:00.000000'),
                ('Muuchstac', 'Muuchstac', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Shikakai', 'Godrej Shikakai', TRUE, '2026-10-29 00:00:00.000000'),
                ('Godrej Ninja', 'Godrej Ninja', TRUE, '2026-10-29 00:00:00.000000'),
                ('ZAP', 'ZAP', TRUE, '2026-10-29 00:00:00.000000'),
                ('Rizz', 'Rizz', TRUE, '2026-10-29 00:00:00.000000'),
                ('Hair lab a Godrej co.', 'Hair lab a Godrej co.', TRUE, '2026-10-29 00:00:00.000000')
                ON DUPLICATE KEY UPDATE Description = VALUES(Description), IsActive = TRUE;
                """);

            migrationBuilder.Sql("""
                INSERT INTO AopDescriptions (Code, Description, IsActive, CreatedAt)
                VALUES
                ('FAST CARD', 'FAST CARD', TRUE, '2026-10-29 00:00:00.000000'),
                ('PROTEKT COMBI', 'PROTEKT COMBI', TRUE, '2026-10-29 00:00:00.000000'),
                ('GK ACT+ LMD', 'GK ACT+ LMD', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT CIK SMALL PACK', 'HIT CIK SMALL PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER SPRAY', 'AER SPRAY', TRUE, '2026-10-29 00:00:00.000000'),
                ('CONF+ SOAPS LARGE PK', 'CONF+ SOAPS LARGE PK', TRUE, '2026-10-29 00:00:00.000000'),
                ('NO1 100GM', 'NO1 100GM', TRUE, '2026-10-29 00:00:00.000000'),
                ('COOL SOAPS LARGE PKS', 'COOL SOAPS LARGE PKS', TRUE, '2026-10-29 00:00:00.000000'),
                ('RAT GLUE', 'RAT GLUE', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXP SHAMPOO SMALL', 'EXP SHAMPOO SMALL', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXP SHAMPOO', 'EXP SHAMPOO', TRUE, '2026-10-29 00:00:00.000000'),
                ('FLOOR CLEANER COMBI', 'FLOOR CLEANER COMBI', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER CAR TWIST', 'AER CAR TWIST', TRUE, '2026-10-29 00:00:00.000000'),
                ('DEO SHOT-KS', 'DEO SHOT-KS', TRUE, '2026-10-29 00:00:00.000000'),
                ('AEROSOL KS RANGE', 'AEROSOL KS RANGE', TRUE, '2026-10-29 00:00:00.000000'),
                ('EDP-PA', 'EDP-PA', TRUE, '2026-10-29 00:00:00.000000'),
                ('HPC SHAVING KIT', 'HPC SHAVING KIT', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER MEMBRANE', 'AER MEMBRANE', TRUE, '2026-10-29 00:00:00.000000'),
                ('NO1 RS 10 MULTIPACK', 'NO1 RS 10 MULTIPACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIME 100GM+', 'LIME 100GM+', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXPORT FG', 'EXPORT FG', TRUE, '2026-10-29 00:00:00.000000'),
                ('NO1 125GM+', 'NO1 125GM+', TRUE, '2026-10-29 00:00:00.000000'),
                ('FLOOR CLEANER REFILL', 'FLOOR CLEANER REFILL', TRUE, '2026-10-29 00:00:00.000000'),
                ('ARSL CLASSIC RANGE', 'ARSL CLASSIC RANGE', TRUE, '2026-10-29 00:00:00.000000'),
                ('TEXTURED PREMIUM', 'TEXTURED PREMIUM', TRUE, '2026-10-29 00:00:00.000000'),
                ('DEO SHOT-PA', 'DEO SHOT-PA', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXP CRÈME', 'EXP CRÈME', TRUE, '2026-10-29 00:00:00.000000'),
                ('GK INCENSE STICKS', 'GK INCENSE STICKS', TRUE, '2026-10-29 00:00:00.000000'),
                ('ARSL SIGNATURE RANGE', 'ARSL SIGNATURE RANGE', TRUE, '2026-10-29 00:00:00.000000'),
                ('GENTEEL-TL', 'GENTEEL-TL', TRUE, '2026-10-29 00:00:00.000000'),
                ('PROTEKT REFILL', 'PROTEKT REFILL', TRUE, '2026-10-29 00:00:00.000000'),
                ('PERSONAL REPELLANT', 'PERSONAL REPELLANT', TRUE, '2026-10-29 00:00:00.000000'),
                ('FOAM BODYWASH', 'FOAM BODYWASH', TRUE, '2026-10-29 00:00:00.000000'),
                ('HONEYMOON KIT', 'HONEYMOON KIT', TRUE, '2026-10-29 00:00:00.000000'),
                ('PROFESSIONAL', 'PROFESSIONAL', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIQUIDS 4X-PA', 'LIQUIDS 4X-PA', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIME RS 10', 'LIME RS 10', TRUE, '2026-10-29 00:00:00.000000'),
                ('HPC POST-SHAVE', 'HPC POST-SHAVE', TRUE, '2026-10-29 00:00:00.000000'),
                ('FAIRGLOW', 'FAIRGLOW', TRUE, '2026-10-29 00:00:00.000000'),
                ('HPC EDC', 'HPC EDC', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT MATIC', 'HIT MATIC', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT ANTIROACH', 'HIT ANTIROACH', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT FIK REG LARGE PK', 'HIT FIK REG LARGE PK', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT CIK LARGE PACK', 'HIT CIK LARGE PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('HPC SOAP 125', 'HPC SOAP 125', TRUE, '2026-10-29 00:00:00.000000'),
                ('SHAVING CREAM', 'SHAVING CREAM', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER POCKET', 'AER POCKET', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT CHALK', 'HIT CHALK', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT ANTIROACH SMALL', 'HIT ANTIROACH SMALL', TRUE, '2026-10-29 00:00:00.000000'),
                ('GK FLASH REFILL', 'GK FLASH REFILL', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXCITE', 'EXCITE', TRUE, '2026-10-29 00:00:00.000000'),
                ('PA-HPC GROOMING KIT', 'PA-HPC GROOMING KIT', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER', 'AER', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER MATIC', 'AER MATIC', TRUE, '2026-10-29 00:00:00.000000'),
                ('EZEE 1 LTR PACK', 'EZEE 1 LTR PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('GK FLASH LMD', 'GK FLASH LMD', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIQUIDS 4X-PA ALM CAN', 'LIQUIDS 4X-PA ALM CAN', TRUE, '2026-10-29 00:00:00.000000'),
                ('ORGASMAX', 'ORGASMAX', TRUE, '2026-10-29 00:00:00.000000'),
                ('THIN', 'THIN', TRUE, '2026-10-29 00:00:00.000000'),
                ('HPC SHAMPOO', 'HPC SHAMPOO', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXP CRÈME SMALL', 'EXP CRÈME SMALL', TRUE, '2026-10-29 00:00:00.000000'),
                ('CSR 100G', 'CSR 100G', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT BAIT', 'HIT BAIT', TRUE, '2026-10-29 00:00:00.000000'),
                ('DEO LOTION PA', 'DEO LOTION PA', TRUE, '2026-10-29 00:00:00.000000'),
                ('FAB LARGE PACK', 'FAB LARGE PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIQUIDS POCKET-KS', 'LIQUIDS POCKET-KS', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIQUID MIST-KS', 'LIQUID MIST-KS', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT RAT SPRAY', 'HIT RAT SPRAY', TRUE, '2026-10-29 00:00:00.000000'),
                ('SPIC TOILET CLEANER', 'SPIC TOILET CLEANER', TRUE, '2026-10-29 00:00:00.000000'),
                ('HAND WASH REFILL', 'HAND WASH REFILL', TRUE, '2026-10-29 00:00:00.000000'),
                ('EZEE LARGE PACK', 'EZEE LARGE PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('EZEE-SACHET', 'EZEE-SACHET', TRUE, '2026-10-29 00:00:00.000000'),
                ('Stella', 'Stella', TRUE, '2026-10-29 00:00:00.000000'),
                ('Muuchstac', 'Muuchstac', TRUE, '2026-10-29 00:00:00.000000'),
                ('CIN TALC', 'CIN TALC', TRUE, '2026-10-29 00:00:00.000000'),
                ('AER PLUG', 'AER PLUG', TRUE, '2026-10-29 00:00:00.000000'),
                ('GK MAT', 'GK MAT', TRUE, '2026-10-29 00:00:00.000000'),
                ('SHIKAKAI', 'SHIKAKAI', TRUE, '2026-10-29 00:00:00.000000'),
                ('TEXTURED', 'TEXTURED', TRUE, '2026-10-29 00:00:00.000000'),
                ('LONGLAST', 'LONGLAST', TRUE, '2026-10-29 00:00:00.000000'),
                ('CONF+ SOAPS RS 10', 'CONF+ SOAPS RS 10', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT RACQUET', 'HIT RACQUET', TRUE, '2026-10-29 00:00:00.000000'),
                ('FAB 1 LTR PACK', 'FAB 1 LTR PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT FIK REG SMALL PK', 'HIT FIK REG SMALL PK', TRUE, '2026-10-29 00:00:00.000000'),
                ('NO1 RS 10 SINGLES', 'NO1 RS 10 SINGLES', TRUE, '2026-10-29 00:00:00.000000'),
                ('EXP ORIG', 'EXP ORIG', TRUE, '2026-10-29 00:00:00.000000'),
                ('CSR 150G', 'CSR 150G', TRUE, '2026-10-29 00:00:00.000000'),
                ('FAB FRONT LOAD', 'FAB FRONT LOAD', TRUE, '2026-10-29 00:00:00.000000'),
                ('GK ACT+ REFILL', 'GK ACT+ REFILL', TRUE, '2026-10-29 00:00:00.000000'),
                ('HIT FIK LIME', 'HIT FIK LIME', TRUE, '2026-10-29 00:00:00.000000'),
                ('FAB 2 LTR PACK', 'FAB 2 LTR PACK', TRUE, '2026-10-29 00:00:00.000000'),
                ('GENTEEL-FL', 'GENTEEL-FL', TRUE, '2026-10-29 00:00:00.000000'),
                ('MAHA JUMBO', 'MAHA JUMBO', TRUE, '2026-10-29 00:00:00.000000'),
                ('CSR 35G', 'CSR 35G', TRUE, '2026-10-29 00:00:00.000000'),
                ('NJ DRY PUP C&MP (EC)', 'NJ DRY PUP C&MP (EC)', TRUE, '2026-10-29 00:00:00.000000'),
                ('COOL SOAPS RS 10', 'COOL SOAPS RS 10', TRUE, '2026-10-29 00:00:00.000000'),
                ('LIME 75GM', 'LIME 75GM', TRUE, '2026-10-29 00:00:00.000000'),
                ('STAIN REMOVER', 'STAIN REMOVER', TRUE, '2026-10-29 00:00:00.000000'),
                ('RIZZ DISH WASH', 'RIZZ DISH WASH', TRUE, '2026-10-29 00:00:00.000000'),
                ('HAIR LAB', 'HAIR LAB', TRUE, '2026-10-29 00:00:00.000000'),
                ('LSC GREEN SHAKTI', 'LSC GREEN SHAKTI', TRUE, '2026-10-29 00:00:00.000000'),
                ('MINI JUMBO', 'MINI JUMBO', TRUE, '2026-10-29 00:00:00.000000'),
                ('FAB-SACHET', 'FAB-SACHET', TRUE, '2026-10-29 00:00:00.000000')
                ON DUPLICATE KEY UPDATE Description = VALUES(Description), IsActive = TRUE;
                """);

            migrationBuilder.Sql("""
                UPDATE Materials AS material
                INNER JOIN (
                SELECT 40019311 AS MaterialNumber, 'GK' AS BrandCode, 'FAST CARD' AS AopCode
                UNION ALL
                SELECT 40031422 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT COMBI' AS AopCode
                UNION ALL
                SELECT 40032838 AS MaterialNumber, 'GK' AS BrandCode, 'GK ACT+ LMD' AS AopCode
                UNION ALL
                SELECT 40034354 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK SMALL PACK' AS AopCode
                UNION ALL
                SELECT 40035371 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40035640 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CONF+ SOAPS LARGE PK' AS AopCode
                UNION ALL
                SELECT 40036555 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40036557 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40036565 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40036892 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40037210 AS MaterialNumber, 'Hit' AS BrandCode, 'RAT GLUE' AS AopCode
                UNION ALL
                SELECT 40037397 AS MaterialNumber, 'GK' AS BrandCode, 'GK ACT+ LMD' AS AopCode
                UNION ALL
                SELECT 40038891 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO SMALL' AS AopCode
                UNION ALL
                SELECT 40038941 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40039426 AS MaterialNumber, 'Magic' AS BrandCode, 'FLOOR CLEANER COMBI' AS AopCode
                UNION ALL
                SELECT 40039568 AS MaterialNumber, 'AER' AS BrandCode, 'AER CAR TWIST' AS AopCode
                UNION ALL
                SELECT 40039574 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CONF+ SOAPS LARGE PK' AS AopCode
                UNION ALL
                SELECT 40040920 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'DEO SHOT-KS' AS AopCode
                UNION ALL
                SELECT 40040950 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40041053 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40041130 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40041135 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAVING KIT' AS AopCode
                UNION ALL
                SELECT 40041189 AS MaterialNumber, 'AER' AS BrandCode, 'AER CAR TWIST' AS AopCode
                UNION ALL
                SELECT 40041315 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40041316 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40041325 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40041399 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40041509 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40041512 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40041513 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40041514 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40041517 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40041526 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40041723 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40041749 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EXPORT FG' AS AopCode
                UNION ALL
                SELECT 40041774 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40041791 AS MaterialNumber, 'AER' AS BrandCode, 'AER CAR TWIST' AS AopCode
                UNION ALL
                SELECT 40041813 AS MaterialNumber, 'AER' AS BrandCode, 'AER CAR TWIST' AS AopCode
                UNION ALL
                SELECT 40041814 AS MaterialNumber, 'AER' AS BrandCode, 'AER CAR TWIST' AS AopCode
                UNION ALL
                SELECT 40041820 AS MaterialNumber, 'Magic' AS BrandCode, 'FLOOR CLEANER REFILL' AS AopCode
                UNION ALL
                SELECT 40041825 AS MaterialNumber, 'AER' AS BrandCode, 'AER CAR TWIST' AS AopCode
                UNION ALL
                SELECT 40041854 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40041862 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL CLASSIC RANGE' AS AopCode
                UNION ALL
                SELECT 40042151 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED PREMIUM' AS AopCode
                UNION ALL
                SELECT 40042202 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'DEO SHOT-PA' AS AopCode
                UNION ALL
                SELECT 40042647 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40043390 AS MaterialNumber, 'GK' AS BrandCode, 'GK INCENSE STICKS' AS AopCode
                UNION ALL
                SELECT 40043424 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40043457 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40043478 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL SIGNATURE RANGE' AS AopCode
                UNION ALL
                SELECT 40043496 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40043533 AS MaterialNumber, 'Genteel' AS BrandCode, 'GENTEEL-TL' AS AopCode
                UNION ALL
                SELECT 40043577 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40043627 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40043896 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40043918 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT COMBI' AS AopCode
                UNION ALL
                SELECT 40043934 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL SIGNATURE RANGE' AS AopCode
                UNION ALL
                SELECT 40043940 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL SIGNATURE RANGE' AS AopCode
                UNION ALL
                SELECT 40043941 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL CLASSIC RANGE' AS AopCode
                UNION ALL
                SELECT 40044016 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40044028 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAVING KIT' AS AopCode
                UNION ALL
                SELECT 40044093 AS MaterialNumber, 'GK' AS BrandCode, 'PERSONAL REPELLANT' AS AopCode
                UNION ALL
                SELECT 40044138 AS MaterialNumber, 'GK' AS BrandCode, 'PERSONAL REPELLANT' AS AopCode
                UNION ALL
                SELECT 40044155 AS MaterialNumber, 'GK' AS BrandCode, 'GK INCENSE STICKS' AS AopCode
                UNION ALL
                SELECT 40049654 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40049658 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40049718 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40049729 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40049857 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'HONEYMOON KIT' AS AopCode
                UNION ALL
                SELECT 40049901 AS MaterialNumber, 'Godrej Professional' AS BrandCode, 'PROFESSIONAL' AS AopCode
                UNION ALL
                SELECT 40049906 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'LIQUIDS 4X-PA' AS AopCode
                UNION ALL
                SELECT 40049909 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME RS 10' AS AopCode
                UNION ALL
                SELECT 40049949 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40050073 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40050107 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40050134 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC POST-SHAVE' AS AopCode
                UNION ALL
                SELECT 40050204 AS MaterialNumber, 'Fresh Glow' AS BrandCode, 'FAIRGLOW' AS AopCode
                UNION ALL
                SELECT 40050288 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC EDC' AS AopCode
                UNION ALL
                SELECT 40050305 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC EDC' AS AopCode
                UNION ALL
                SELECT 40050306 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC EDC' AS AopCode
                UNION ALL
                SELECT 40050342 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT MATIC' AS AopCode
                UNION ALL
                SELECT 40050411 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT ANTIROACH' AS AopCode
                UNION ALL
                SELECT 40050424 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40050605 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40050608 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40050609 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40050629 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40050696 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40050728 AS MaterialNumber, 'Cin Male Grmg Shaving cream' AS BrandCode, 'SHAVING CREAM' AS AopCode
                UNION ALL
                SELECT 40050754 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40050858 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40050877 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CHALK' AS AopCode
                UNION ALL
                SELECT 40050878 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40056208 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40056350 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40056378 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT ANTIROACH SMALL' AS AopCode
                UNION ALL
                SELECT 40056404 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40056434 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40056589 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40056592 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40056730 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40056810 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40056853 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40056855 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40056885 AS MaterialNumber, 'AER' AS BrandCode, 'AER' AS AopCode
                UNION ALL
                SELECT 40056899 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40056903 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40056914 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40056930 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40057079 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40057135 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40057243 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40057277 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40057365 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE 1 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40057435 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40057534 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40057536 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40057848 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40057849 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40057884 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40057928 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40057966 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40058047 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40058316 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'LIQUIDS 4X-PA ALM CAN' AS AopCode
                UNION ALL
                SELECT 40058428 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40058543 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'ORGASMAX' AS AopCode
                UNION ALL
                SELECT 40058544 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'THIN' AS AopCode
                UNION ALL
                SELECT 40058547 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'ORGASMAX' AS AopCode
                UNION ALL
                SELECT 40058636 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40058677 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME SMALL' AS AopCode
                UNION ALL
                SELECT 40058699 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40059094 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 100G' AS AopCode
                UNION ALL
                SELECT 40059169 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED PREMIUM' AS AopCode
                UNION ALL
                SELECT 40059216 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL CLASSIC RANGE' AS AopCode
                UNION ALL
                SELECT 40059237 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED PREMIUM' AS AopCode
                UNION ALL
                SELECT 40059248 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'THIN' AS AopCode
                UNION ALL
                SELECT 40059249 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40059251 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'THIN' AS AopCode
                UNION ALL
                SELECT 40059263 AS MaterialNumber, 'GK' AS BrandCode, 'GK INCENSE STICKS' AS AopCode
                UNION ALL
                SELECT 40059268 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'THIN' AS AopCode
                UNION ALL
                SELECT 40059327 AS MaterialNumber, 'GK' AS BrandCode, 'GK INCENSE STICKS' AS AopCode
                UNION ALL
                SELECT 40059400 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059406 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059407 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059421 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059425 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED PREMIUM' AS AopCode
                UNION ALL
                SELECT 40059461 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40059464 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40059476 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED PREMIUM' AS AopCode
                UNION ALL
                SELECT 40059477 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'THIN' AS AopCode
                UNION ALL
                SELECT 40059499 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT ANTIROACH SMALL' AS AopCode
                UNION ALL
                SELECT 40059500 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'THIN' AS AopCode
                UNION ALL
                SELECT 40059534 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT BAIT' AS AopCode
                UNION ALL
                SELECT 40059565 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059567 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059569 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40059571 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059574 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'DEO LOTION PA' AS AopCode
                UNION ALL
                SELECT 40059587 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40059656 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LIQUIDS POCKET-KS' AS AopCode
                UNION ALL
                SELECT 40059677 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LIQUID MIST-KS' AS AopCode
                UNION ALL
                SELECT 40059679 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LIQUID MIST-KS' AS AopCode
                UNION ALL
                SELECT 40059685 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL CLASSIC RANGE' AS AopCode
                UNION ALL
                SELECT 40059716 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059731 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059732 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40059737 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40059739 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40059764 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT RAT SPRAY' AS AopCode
                UNION ALL
                SELECT 40059774 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40059775 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40059778 AS MaterialNumber, 'SPIC' AS BrandCode, 'SPIC TOILET CLEANER' AS AopCode
                UNION ALL
                SELECT 40059779 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40059966 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40060032 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40060033 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40060034 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40060122 AS MaterialNumber, 'Protekt' AS BrandCode, 'HAND WASH REFILL' AS AopCode
                UNION ALL
                SELECT 40060184 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40060217 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40060218 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40060220 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40060223 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40060243 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 100G' AS AopCode
                UNION ALL
                SELECT 40060252 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40060282 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC POST-SHAVE' AS AopCode
                UNION ALL
                SELECT 40060285 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40060288 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40060339 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE-SACHET' AS AopCode
                UNION ALL
                SELECT 40060343 AS MaterialNumber, 'Fresh Glow' AS BrandCode, 'FAIRGLOW' AS AopCode
                UNION ALL
                SELECT 40060384 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC POST-SHAVE' AS AopCode
                UNION ALL
                SELECT 40060426 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'LIQUIDS 4X-PA ALM CAN' AS AopCode
                UNION ALL
                SELECT 40060427 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40060449 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40060450 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40060509 AS MaterialNumber, 'Magic' AS BrandCode, 'FLOOR CLEANER REFILL' AS AopCode
                UNION ALL
                SELECT 40060514 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40060517 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40060535 AS MaterialNumber, 'Godrej Shaving Cream' AS BrandCode, 'SHAVING CREAM' AS AopCode
                UNION ALL
                SELECT 40060536 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE 1 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40060624 AS MaterialNumber, 'Stella' AS BrandCode, 'Stella' AS AopCode
                UNION ALL
                SELECT 40060649 AS MaterialNumber, 'Genteel' AS BrandCode, 'GENTEEL-TL' AS AopCode
                UNION ALL
                SELECT 40060737 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC POST-SHAVE' AS AopCode
                UNION ALL
                SELECT 40060738 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC POST-SHAVE' AS AopCode
                UNION ALL
                SELECT 40060740 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40060742 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40060775 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40060776 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40060777 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40060778 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40060779 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40060792 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40060887 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40060914 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40061013 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40061029 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40061075 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LIQUIDS POCKET-KS' AS AopCode
                UNION ALL
                SELECT 40061076 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LIQUID MIST-KS' AS AopCode
                UNION ALL
                SELECT 40061084 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CIN TALC' AS AopCode
                UNION ALL
                SELECT 40061093 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAVING KIT' AS AopCode
                UNION ALL
                SELECT 40061162 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061164 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061167 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061179 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061192 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061226 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAVING KIT' AS AopCode
                UNION ALL
                SELECT 40061363 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAVING KIT' AS AopCode
                UNION ALL
                SELECT 40061447 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40061498 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME SMALL' AS AopCode
                UNION ALL
                SELECT 40061510 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061728 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'ORGASMAX' AS AopCode
                UNION ALL
                SELECT 40061847 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40061879 AS MaterialNumber, 'Fresh Glow' AS BrandCode, 'FAIRGLOW' AS AopCode
                UNION ALL
                SELECT 40062000 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'DEO SHOT-KS' AS AopCode
                UNION ALL
                SELECT 40062014 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40062017 AS MaterialNumber, 'GK' AS BrandCode, 'GK MAT' AS AopCode
                UNION ALL
                SELECT 40062018 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40062022 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO SMALL' AS AopCode
                UNION ALL
                SELECT 40062023 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME SMALL' AS AopCode
                UNION ALL
                SELECT 40062024 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME SMALL' AS AopCode
                UNION ALL
                SELECT 40062071 AS MaterialNumber, 'Godrej Shikakai' AS BrandCode, 'SHIKAKAI' AS AopCode
                UNION ALL
                SELECT 40062265 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40062271 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40062301 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40062325 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062326 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062328 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED' AS AopCode
                UNION ALL
                SELECT 40062334 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062336 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062339 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LONGLAST' AS AopCode
                UNION ALL
                SELECT 40062340 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062342 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062343 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LONGLAST' AS AopCode
                UNION ALL
                SELECT 40062345 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40062347 AS MaterialNumber, 'SPIC' AS BrandCode, 'SPIC TOILET CLEANER' AS AopCode
                UNION ALL
                SELECT 40062349 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED' AS AopCode
                UNION ALL
                SELECT 40062385 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40062391 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40062464 AS MaterialNumber, 'AER' AS BrandCode, 'AER MEMBRANE' AS AopCode
                UNION ALL
                SELECT 40062506 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40062512 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME RS 10' AS AopCode
                UNION ALL
                SELECT 40062514 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CONF+ SOAPS RS 10' AS AopCode
                UNION ALL
                SELECT 40062517 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LONGLAST' AS AopCode
                UNION ALL
                SELECT 40062518 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED' AS AopCode
                UNION ALL
                SELECT 40062519 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED' AS AopCode
                UNION ALL
                SELECT 40062523 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062524 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'EXCITE' AS AopCode
                UNION ALL
                SELECT 40062531 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40062532 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40062536 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED' AS AopCode
                UNION ALL
                SELECT 40062549 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'LIQUID MIST-KS' AS AopCode
                UNION ALL
                SELECT 40062828 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40062845 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40062858 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'TEXTURED' AS AopCode
                UNION ALL
                SELECT 40062935 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40062936 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT RACQUET' AS AopCode
                UNION ALL
                SELECT 40062938 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 1 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40063016 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063017 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063018 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063020 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063026 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063045 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG SMALL PK' AS AopCode
                UNION ALL
                SELECT 40063052 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40063122 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'LIQUIDS 4X-PA' AS AopCode
                UNION ALL
                SELECT 40063138 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'LIQUIDS 4X-PA' AS AopCode
                UNION ALL
                SELECT 40063144 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SHAVING KIT' AS AopCode
                UNION ALL
                SELECT 40063168 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063169 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063172 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063174 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063178 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063180 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063193 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40063194 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40063198 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40063254 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40063258 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 SINGLES' AS AopCode
                UNION ALL
                SELECT 40063273 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40063275 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063277 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063279 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40063282 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40063289 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40063390 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063391 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063408 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063417 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP ORIG' AS AopCode
                UNION ALL
                SELECT 40063422 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063423 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063424 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063425 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063426 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063427 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME' AS AopCode
                UNION ALL
                SELECT 40063445 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40063446 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063447 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40063451 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 150G' AS AopCode
                UNION ALL
                SELECT 40063457 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 100G' AS AopCode
                UNION ALL
                SELECT 40063469 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB FRONT LOAD' AS AopCode
                UNION ALL
                SELECT 40063472 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 1 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40063473 AS MaterialNumber, 'GK' AS BrandCode, 'GK ACT+ REFILL' AS AopCode
                UNION ALL
                SELECT 40063476 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063478 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063481 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK LIME' AS AopCode
                UNION ALL
                SELECT 40063483 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK SMALL PACK' AS AopCode
                UNION ALL
                SELECT 40063484 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40063485 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK LIME' AS AopCode
                UNION ALL
                SELECT 40063486 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK LIME' AS AopCode
                UNION ALL
                SELECT 40063487 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063488 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40063489 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40063490 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40063491 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG LARGE PK' AS AopCode
                UNION ALL
                SELECT 40063492 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063493 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063494 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40063500 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT FIK REG SMALL PK' AS AopCode
                UNION ALL
                SELECT 40063504 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40063506 AS MaterialNumber, 'Cinthol' AS BrandCode, 'FOAM BODYWASH' AS AopCode
                UNION ALL
                SELECT 40063527 AS MaterialNumber, 'SPIC' AS BrandCode, 'SPIC TOILET CLEANER' AS AopCode
                UNION ALL
                SELECT 40063537 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40063539 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL SIGNATURE RANGE' AS AopCode
                UNION ALL
                SELECT 40063555 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40063564 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 2 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40063568 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB FRONT LOAD' AS AopCode
                UNION ALL
                SELECT 40063573 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063576 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 1 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40063577 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40063579 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40063581 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40063584 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40063593 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40063597 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40063602 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063603 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40063604 AS MaterialNumber, 'Genteel' AS BrandCode, 'GENTEEL-TL' AS AopCode
                UNION ALL
                SELECT 40063605 AS MaterialNumber, 'Genteel' AS BrandCode, 'GENTEEL-FL' AS AopCode
                UNION ALL
                SELECT 40063606 AS MaterialNumber, 'Genteel' AS BrandCode, 'GENTEEL-TL' AS AopCode
                UNION ALL
                SELECT 40063609 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40063613 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40063617 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40063618 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063623 AS MaterialNumber, 'GK' AS BrandCode, 'MAHA JUMBO' AS AopCode
                UNION ALL
                SELECT 40063637 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME SMALL' AS AopCode
                UNION ALL
                SELECT 40063638 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 100G' AS AopCode
                UNION ALL
                SELECT 40063685 AS MaterialNumber, 'Protekt' AS BrandCode, 'HAND WASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063686 AS MaterialNumber, 'Protekt' AS BrandCode, 'HAND WASH REFILL' AS AopCode
                UNION ALL
                SELECT 40063688 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063690 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40063691 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40063698 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 35G' AS AopCode
                UNION ALL
                SELECT 40063699 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063700 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063715 AS MaterialNumber, 'AER' AS BrandCode, 'AER MATIC' AS AopCode
                UNION ALL
                SELECT 40063738 AS MaterialNumber, 'Godrej Ninja' AS BrandCode, 'NJ DRY PUP C&MP (EC)' AS AopCode
                UNION ALL
                SELECT 40063747 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063752 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063753 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063754 AS MaterialNumber, 'Godrej Expert' AS BrandCode, 'EXP CRÈME SMALL' AS AopCode
                UNION ALL
                SELECT 40063756 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME RS 10' AS AopCode
                UNION ALL
                SELECT 40063758 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS RS 10' AS AopCode
                UNION ALL
                SELECT 40063767 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40063781 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 75GM' AS AopCode
                UNION ALL
                SELECT 40063787 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063788 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063794 AS MaterialNumber, 'AER' AS BrandCode, 'AER SPRAY' AS AopCode
                UNION ALL
                SELECT 40063808 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40063810 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40063816 AS MaterialNumber, 'AER' AS BrandCode, 'AER POCKET' AS AopCode
                UNION ALL
                SELECT 40063823 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40063845 AS MaterialNumber, 'AER' AS BrandCode, 'EXPORT FG' AS AopCode
                UNION ALL
                SELECT 40063900 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40063901 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40063903 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40063908 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL CLASSIC RANGE' AS AopCode
                UNION ALL
                SELECT 40063909 AS MaterialNumber, 'KamaSutra' AS BrandCode, 'AEROSOL KS RANGE' AS AopCode
                UNION ALL
                SELECT 40063953 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 2 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40063954 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40063964 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40063965 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40063966 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL CLASSIC RANGE' AS AopCode
                UNION ALL
                SELECT 40063967 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40063969 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'PA-HPC GROOMING KIT' AS AopCode
                UNION ALL
                SELECT 40064103 AS MaterialNumber, 'GK' AS BrandCode, 'GK ACT+ REFILL' AS AopCode
                UNION ALL
                SELECT 40064104 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40064105 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40064108 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40064109 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40064154 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40064157 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 75GM' AS AopCode
                UNION ALL
                SELECT 40064163 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40064164 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40064219 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40064220 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40064227 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40064331 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'ARSL SIGNATURE RANGE' AS AopCode
                UNION ALL
                SELECT 40064422 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EXPORT FG' AS AopCode
                UNION ALL
                SELECT 40064427 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40064428 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40064497 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40064503 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40064518 AS MaterialNumber, 'Godrej Selfie' AS BrandCode, 'EXP SHAMPOO' AS AopCode
                UNION ALL
                SELECT 40064535 AS MaterialNumber, 'Protekt' AS BrandCode, 'HAND WASH REFILL' AS AopCode
                UNION ALL
                SELECT 40064537 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40064558 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40064562 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40064661 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40064682 AS MaterialNumber, 'ZAP' AS BrandCode, 'STAIN REMOVER' AS AopCode
                UNION ALL
                SELECT 40064683 AS MaterialNumber, 'ZAP' AS BrandCode, 'STAIN REMOVER' AS AopCode
                UNION ALL
                SELECT 40064684 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40064685 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK SMALL PACK' AS AopCode
                UNION ALL
                SELECT 40064687 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40064688 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT CIK LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40064710 AS MaterialNumber, 'Hit' AS BrandCode, 'HIT RACQUET' AS AopCode
                UNION ALL
                SELECT 40064727 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40064728 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40064729 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40064824 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40064825 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 125GM+' AS AopCode
                UNION ALL
                SELECT 40064841 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40064844 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40064855 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 100GM' AS AopCode
                UNION ALL
                SELECT 40064876 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB FRONT LOAD' AS AopCode
                UNION ALL
                SELECT 40064886 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40064893 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 2 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40064905 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40064907 AS MaterialNumber, 'Hit' AS BrandCode, 'RAT GLUE' AS AopCode
                UNION ALL
                SELECT 40064913 AS MaterialNumber, 'Hit' AS BrandCode, 'RAT GLUE' AS AopCode
                UNION ALL
                SELECT 40064918 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 SINGLES' AS AopCode
                UNION ALL
                SELECT 40064923 AS MaterialNumber, 'No1' AS BrandCode, 'NO1 RS 10 MULTIPACK' AS AopCode
                UNION ALL
                SELECT 40064945 AS MaterialNumber, 'Rizz' AS BrandCode, 'RIZZ DISH WASH' AS AopCode
                UNION ALL
                SELECT 40064953 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 35G' AS AopCode
                UNION ALL
                SELECT 40064955 AS MaterialNumber, 'Cinthol' AS BrandCode, 'LIME 100GM+' AS AopCode
                UNION ALL
                SELECT 40064956 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CONF+ SOAPS LARGE PK' AS AopCode
                UNION ALL
                SELECT 40064990 AS MaterialNumber, 'Hair lab a Godrej co.' AS BrandCode, 'HAIR LAB' AS AopCode
                UNION ALL
                SELECT 40064994 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CONF+ SOAPS LARGE PK' AS AopCode
                UNION ALL
                SELECT 40064998 AS MaterialNumber, 'Cinthol' AS BrandCode, 'COOL SOAPS LARGE PKS' AS AopCode
                UNION ALL
                SELECT 40065000 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40065002 AS MaterialNumber, 'Magic' AS BrandCode, 'PROTEKT REFILL' AS AopCode
                UNION ALL
                SELECT 40065039 AS MaterialNumber, 'Cinthol' AS BrandCode, 'CSR 100G' AS AopCode
                UNION ALL
                SELECT 40065137 AS MaterialNumber, 'GK' AS BrandCode, 'GK INCENSE STICKS' AS AopCode
                UNION ALL
                SELECT 40065139 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'HPC SOAP 125' AS AopCode
                UNION ALL
                SELECT 40065172 AS MaterialNumber, 'Genteel' AS BrandCode, 'GENTEEL-TL' AS AopCode
                UNION ALL
                SELECT 40065236 AS MaterialNumber, 'Rizz' AS BrandCode, 'RIZZ DISH WASH' AS AopCode
                UNION ALL
                SELECT 40065250 AS MaterialNumber, 'GK' AS BrandCode, 'MAHA JUMBO' AS AopCode
                UNION ALL
                SELECT 40065256 AS MaterialNumber, 'GK' AS BrandCode, 'LSC GREEN SHAKTI' AS AopCode
                UNION ALL
                SELECT 40065261 AS MaterialNumber, 'GK' AS BrandCode, 'MAHA JUMBO' AS AopCode
                UNION ALL
                SELECT 40065274 AS MaterialNumber, 'GK' AS BrandCode, 'MINI JUMBO' AS AopCode
                UNION ALL
                SELECT 40065277 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB 1 LTR PACK' AS AopCode
                UNION ALL
                SELECT 40065278 AS MaterialNumber, 'AER' AS BrandCode, 'AER PLUG' AS AopCode
                UNION ALL
                SELECT 40065287 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40065291 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40065297 AS MaterialNumber, 'Muuchstac' AS BrandCode, 'Muuchstac' AS AopCode
                UNION ALL
                SELECT 40065334 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40065336 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40065337 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40065344 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40065345 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40065346 AS MaterialNumber, 'GK' AS BrandCode, 'GK ACT+ REFILL' AS AopCode
                UNION ALL
                SELECT 40065352 AS MaterialNumber, 'GK' AS BrandCode, 'GK ACT+ REFILL' AS AopCode
                UNION ALL
                SELECT 40065374 AS MaterialNumber, 'Godrej Fab' AS BrandCode, 'FAB-SACHET' AS AopCode
                UNION ALL
                SELECT 40065410 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH REFILL' AS AopCode
                UNION ALL
                SELECT 40065411 AS MaterialNumber, 'Ezee' AS BrandCode, 'EZEE LARGE PACK' AS AopCode
                UNION ALL
                SELECT 40065477 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40065478 AS MaterialNumber, 'Park Avenue' AS BrandCode, 'EDP-PA' AS AopCode
                UNION ALL
                SELECT 40065917 AS MaterialNumber, 'GK' AS BrandCode, 'GK FLASH LMD' AS AopCode
                UNION ALL
                SELECT 40065953 AS MaterialNumber, 'Hit' AS BrandCode, 'RAT GLUE' AS AopCode
                ) AS mapping ON mapping.MaterialNumber = material.MaterialNumber
                INNER JOIN Brands AS brand ON brand.Code = mapping.BrandCode
                INNER JOIN AopDescriptions AS aop ON aop.Code = mapping.AopCode
                SET material.BrandId = brand.Id,
                    material.AopDescriptionId = aop.Id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_AopDescriptions_AopDescriptionId",
                table: "Materials");

            migrationBuilder.DropForeignKey(
                name: "FK_Materials_Brands_BrandId",
                table: "Materials");

            migrationBuilder.DropTable(
                name: "AopDescriptions");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropIndex(
                name: "IX_Materials_AopDescriptionId",
                table: "Materials");

            migrationBuilder.DropIndex(
                name: "IX_Materials_BrandId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "AopDescriptionId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "Materials");
        }
    }
}
