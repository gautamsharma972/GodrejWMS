using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Re-keys inward put-away location preferences from Brand to AOP Description.
    ///
    /// EF scaffolds this as drop + create, which would silently delete every mapping, so Up/Down are
    /// hand-written to carry the data across:
    ///  * each Brand mapping becomes one AOP mapping per distinct AOP Description found on that Brand's
    ///    Materials (same location range, same IsActive, same audit columns);
    ///  * a Brand mapping whose Brand has no Material with an AOP Description cannot be converted - it
    ///    is NOT migrated, and is listed in AopDescriptionLocationPreferenceMigrationExceptions;
    ///  * identical (AOP, range) rows produced by two Brands sharing an AOP are merged (reported);
    ///  * rows whose active ranges overlap for the same AOP Description on the same rack are resolved
    ///    keep-earliest: the later row is DEACTIVATED, not deleted, and reported;
    ///  * BrandId stays on the new table as a nullable, deprecated column so Down can restore exactly.
    /// Review the exceptions table after running. Down restores the old table from it; rows created in
    /// the UI after this migration have no Brand and cannot be restored.
    /// </summary>
    public partial class AopDescriptionLocationPreferences : Migration
    {
        private const string T = "AopDescriptionLocationPreferences";
        private const string Ex = "AopDescriptionLocationPreferenceMigrationExceptions";
        private const string Stage = "AopDescriptionLocationPreferenceMigrationStage";

        private static string Overlap(string fa, string ta, string fb, string tb) =>
            $"{fa}.RackId = {fb}.RackId AND {fa}.`Column` <= {tb}.`Column` AND {ta}.`Column` >= {fb}.`Column` " +
            $"AND {fa}.`Level` <= {tb}.`Level` AND {ta}.`Level` >= {fb}.`Level`";

        /// <summary>Later active rows that overlap an earlier active row (same AOP, same rack). With
        /// rootsOnly, the earlier row must itself have no earlier overlapping active row, so a row is
        /// only ever displaced by a row that survives (no cascade of needless deactivations).</summary>
        private static string Victims(bool rootsOnly) => $@"
SELECT n2.Id AS VictimId, MIN(n1.Id) AS RootId
FROM {T} n2
JOIN PalletPositions f2 ON f2.Id = n2.FromPalletPositionId
JOIN PalletPositions t2 ON t2.Id = n2.ToPalletPositionId
JOIN {T} n1 ON n1.AopDescriptionId = n2.AopDescriptionId AND n1.IsActive = 1 AND n1.Id < n2.Id
JOIN PalletPositions f1 ON f1.Id = n1.FromPalletPositionId
JOIN PalletPositions t1 ON t1.Id = n1.ToPalletPositionId
WHERE n2.IsActive = 1 AND {Overlap("f1", "t1", "f2", "t2")}
{(rootsOnly ? $@"AND NOT EXISTS (
    SELECT 1 FROM {T} n0
    JOIN PalletPositions f0 ON f0.Id = n0.FromPalletPositionId
    JOIN PalletPositions t0 ON t0.Id = n0.ToPalletPositionId
    WHERE n0.AopDescriptionId = n1.AopDescriptionId AND n0.IsActive = 1 AND n0.Id < n1.Id AND {Overlap("f0", "t0", "f1", "t1")})" : "")}
GROUP BY n2.Id";

        private static void DeactivateOverlaps(MigrationBuilder mb, bool rootsOnly)
        {
            var victims = Victims(rootsOnly);
            mb.Sql($@"
INSERT INTO {Ex} (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'Deactivated',
       CONCAT('Overlaps active mapping #', v.RootId, ' for the same AOP Description on the same rack; deactivated. Edit one of the ranges, then re-activate.'),
       NULL, n.Id, n.BrandId, b.Code, n.AopDescriptionId, a.Code,
       n.FromPalletPositionId, n.ToPalletPositionId, pf.LocationCode, pt.LocationCode, 1, UTC_TIMESTAMP(6)
FROM ({victims}) v
JOIN {T} n ON n.Id = v.VictimId
LEFT JOIN Brands b ON b.Id = n.BrandId
JOIN AopDescriptions a ON a.Id = n.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = n.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = n.ToPalletPositionId;");
            mb.Sql($"UPDATE {T} SET IsActive = 0 WHERE Id IN (SELECT VictimId FROM ({victims}) vv);");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AopDescriptionLocationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AopDescriptionId = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<int>(type: "int", nullable: true),
                    FromPalletPositionId = table.Column<int>(type: "int", nullable: false),
                    ToPalletPositionId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_AopDescriptionLocationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AopDescriptionLocationPreferences_AopDescriptions_AopDescrip~",
                        column: x => x.AopDescriptionId,
                        principalTable: "AopDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AopDescriptionLocationPreferences_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AopDescriptionLocationPreferences_PalletPositions_FromPallet~",
                        column: x => x.FromPalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AopDescriptionLocationPreferences_PalletPositions_ToPalletPo~",
                        column: x => x.ToPalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AopDescriptionLocationPreferences_AopDescriptionId_FromPalle~",
                table: "AopDescriptionLocationPreferences",
                columns: new[] { "AopDescriptionId", "FromPalletPositionId", "ToPalletPositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AopDescriptionLocationPreferences_BrandId",
                table: "AopDescriptionLocationPreferences",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_AopDescriptionLocationPreferences_FromPalletPositionId",
                table: "AopDescriptionLocationPreferences",
                column: "FromPalletPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_AopDescriptionLocationPreferences_ToPalletPositionId",
                table: "AopDescriptionLocationPreferences",
                column: "ToPalletPositionId");

            migrationBuilder.CreateTable(
                name: Ex,
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false).Annotation("MySql:CharSet", "utf8mb4"),
                    SourcePreferenceId = table.Column<int>(type: "int", nullable: true),
                    TargetPreferenceId = table.Column<int>(type: "int", nullable: true),
                    BrandId = table.Column<int>(type: "int", nullable: true),
                    BrandCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                    AopDescriptionId = table.Column<int>(type: "int", nullable: true),
                    AopCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                    FromPalletPositionId = table.Column<int>(type: "int", nullable: false),
                    ToPalletPositionId = table.Column<int>(type: "int", nullable: false),
                    FromLocationCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                    ToLocationCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true).Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_" + Ex, x => x.Id))
                .Annotation("MySql:CharSet", "utf8mb4");

            // Stage: every (old mapping x distinct AOP Description of that Brand's Materials) pair.
            migrationBuilder.Sql($@"
CREATE TABLE {Stage} AS
SELECT p.Id AS PrefId, p.BrandId, a.AopDescriptionId, p.FromPalletPositionId, p.ToPalletPositionId,
       p.IsActive, p.CreatedAt, p.CreatedByUserId, p.UpdatedAt, p.UpdatedByUserId
FROM BrandLocationPreferences p
JOIN (SELECT DISTINCT BrandId, AopDescriptionId FROM Materials
      WHERE BrandId IS NOT NULL AND AopDescriptionId IS NOT NULL) a ON a.BrandId = p.BrandId;");

            // One row per (AOP, range): the earliest old mapping wins when two Brands share an AOP.
            migrationBuilder.Sql($@"
INSERT INTO {T} (AopDescriptionId, BrandId, FromPalletPositionId, ToPalletPositionId, IsActive,
                 CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId)
SELECT s.AopDescriptionId, s.BrandId, s.FromPalletPositionId, s.ToPalletPositionId, s.IsActive,
       s.CreatedAt, s.CreatedByUserId, s.UpdatedAt, s.UpdatedByUserId
FROM {Stage} s
WHERE s.PrefId = (SELECT MIN(x.PrefId) FROM {Stage} x
                  WHERE x.AopDescriptionId = s.AopDescriptionId
                    AND x.FromPalletPositionId = s.FromPalletPositionId AND x.ToPalletPositionId = s.ToPalletPositionId)
ORDER BY s.PrefId, s.AopDescriptionId;");

            // Report 1: old mappings whose Brand has no Material with an AOP Description - not migrated.
            migrationBuilder.Sql($@"
INSERT INTO {Ex} (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'NotMigrated', 'Brand has no Material with an AOP Description, so this mapping cannot be converted. Create an AOP Description mapping manually if it is still needed.',
       p.Id, NULL, p.BrandId, b.Code, NULL, NULL, p.FromPalletPositionId, p.ToPalletPositionId, pf.LocationCode, pt.LocationCode, p.IsActive, UTC_TIMESTAMP(6)
FROM BrandLocationPreferences p
JOIN Brands b ON b.Id = p.BrandId
JOIN PalletPositions pf ON pf.Id = p.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = p.ToPalletPositionId
WHERE NOT EXISTS (SELECT 1 FROM Materials m WHERE m.BrandId = p.BrandId AND m.AopDescriptionId IS NOT NULL);");

            // Report 2: identical (AOP, range) pairs merged because two Brands share the AOP Description.
            migrationBuilder.Sql($@"
INSERT INTO {Ex} (`Action`, Reason, SourcePreferenceId, TargetPreferenceId, BrandId, BrandCode, AopDescriptionId, AopCode,
                  FromPalletPositionId, ToPalletPositionId, FromLocationCode, ToLocationCode, IsActive, CreatedAt)
SELECT 'MergedDuplicate', CONCAT('Same AOP Description and range as the mapping migrated from old mapping #', w.WinnerId, '; merged.'),
       s.PrefId, NULL, s.BrandId, b.Code, s.AopDescriptionId, a.Code, s.FromPalletPositionId, s.ToPalletPositionId, pf.LocationCode, pt.LocationCode, s.IsActive, UTC_TIMESTAMP(6)
FROM {Stage} s
JOIN (SELECT AopDescriptionId, FromPalletPositionId, ToPalletPositionId, MIN(PrefId) AS WinnerId FROM {Stage}
      GROUP BY AopDescriptionId, FromPalletPositionId, ToPalletPositionId) w
  ON w.AopDescriptionId = s.AopDescriptionId AND w.FromPalletPositionId = s.FromPalletPositionId AND w.ToPalletPositionId = s.ToPalletPositionId
JOIN Brands b ON b.Id = s.BrandId
JOIN AopDescriptions a ON a.Id = s.AopDescriptionId
JOIN PalletPositions pf ON pf.Id = s.FromPalletPositionId
JOIN PalletPositions pt ON pt.Id = s.ToPalletPositionId
WHERE s.PrefId <> w.WinnerId;");

            // Report 3: overlapping active ranges for the same AOP Description - keep earliest, deactivate later.
            for (var pass = 0; pass < 8; pass++)
            {
                DeactivateOverlaps(migrationBuilder, rootsOnly: true);
            }
            DeactivateOverlaps(migrationBuilder, rootsOnly: false); // guarantee: no active overlaps remain

            migrationBuilder.Sql($"DROP TABLE {Stage};");
            migrationBuilder.DropTable(
                name: "BrandLocationPreferences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandLocationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BrandId = table.Column<int>(type: "int", nullable: false),
                    FromPalletPositionId = table.Column<int>(type: "int", nullable: false),
                    ToPalletPositionId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandLocationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandLocationPreferences_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandLocationPreferences_PalletPositions_FromPalletPositionId",
                        column: x => x.FromPalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandLocationPreferences_PalletPositions_ToPalletPositionId",
                        column: x => x.ToPalletPositionId,
                        principalTable: "PalletPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BrandLocationPreferences_BrandId_FromPalletPositionId_ToPall~",
                table: "BrandLocationPreferences",
                columns: new[] { "BrandId", "FromPalletPositionId", "ToPalletPositionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrandLocationPreferences_FromPalletPositionId",
                table: "BrandLocationPreferences",
                column: "FromPalletPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandLocationPreferences_ToPalletPositionId",
                table: "BrandLocationPreferences",
                column: "ToPalletPositionId");

            // Brand-keyed rows come back from the deprecated BrandId column: one old mapping per
            // (Brand, range). A row is active again if any copy was active or the migration deactivated it.
            migrationBuilder.Sql($@"
INSERT INTO BrandLocationPreferences (BrandId, FromPalletPositionId, ToPalletPositionId, IsActive,
                                      CreatedAt, CreatedByUserId, UpdatedAt, UpdatedByUserId)
SELECT t.BrandId, t.FromPalletPositionId, t.ToPalletPositionId, g.Active,
       t.CreatedAt, t.CreatedByUserId, t.UpdatedAt, t.UpdatedByUserId
FROM {T} t
JOIN (SELECT MIN(r.Id) AS Id,
             MAX(CASE WHEN r.IsActive = 1 OR r.Id IN (SELECT e.TargetPreferenceId FROM {Ex} e WHERE e.`Action` = 'Deactivated')
                      THEN 1 ELSE 0 END) AS Active
      FROM {T} r WHERE r.BrandId IS NOT NULL
      GROUP BY r.BrandId, r.FromPalletPositionId, r.ToPalletPositionId) g ON g.Id = t.Id;");

            // Mappings that were never migrated (Brand without AOP data) or merged as duplicates come back unchanged.
            migrationBuilder.Sql($@"
INSERT INTO BrandLocationPreferences (BrandId, FromPalletPositionId, ToPalletPositionId, IsActive, CreatedAt)
SELECT e.BrandId, e.FromPalletPositionId, e.ToPalletPositionId, e.IsActive, UTC_TIMESTAMP(6)
FROM {Ex} e
WHERE e.`Action` IN ('NotMigrated', 'MergedDuplicate') AND e.BrandId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM BrandLocationPreferences p
                  WHERE p.BrandId = e.BrandId AND p.FromPalletPositionId = e.FromPalletPositionId AND p.ToPalletPositionId = e.ToPalletPositionId);");

            migrationBuilder.DropTable(
                name: Ex);

            migrationBuilder.DropTable(
                name: "AopDescriptionLocationPreferences");
        }
    }
}
