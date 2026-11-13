using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GodrejWMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitZoneType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The GIT zone owns the one persistent "set aside as GIT" location created per warehouse
            // (see GitLocationService). Idempotent, same pattern as the Overflow zone type seed in
            // 20261108101407_AddVirtualLocationsAndPostOnUpload.cs. Id is a fixed, far-out-of-range
            // value (ZoneTypeIds.Git), not the next sequential one, so it can never collide with a
            // zone type a warehouse admin already created through the UI.
            migrationBuilder.Sql(@"
                INSERT INTO ZoneTypes (Id, Code, DisplayName, Description, SortOrder, IsActive, CreatedAt)
                SELECT 9001, 'GIT', 'GIT', 'Picker set-aside stock, not yet racked', 91, 1, UTC_TIMESTAMP(6)
                FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM ZoneTypes WHERE Code = 'GIT');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ZoneTypes WHERE Code = 'GIT' AND NOT EXISTS (SELECT 1 FROM PalletPositions x WHERE x.ZoneType = ZoneTypes.Id);
            ");
        }
    }
}
