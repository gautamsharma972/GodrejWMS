using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>
/// Covers §25's concurrency requirement at the point it was previously unguarded: two inward
/// submissions racing for the same freshly-empty pallet position. <see cref="SubmitInwardHandler"/>
/// now increments <see cref="PalletPosition.RowVersion"/> for every position it reserves against
/// and retries on <see cref="DbUpdateConcurrencyException"/> (see the retry loop's own comments
/// in SubmitInward.cs for why the full retry-recovers-cleanly path isn't also asserted here: it
/// depends on relational-provider transaction atomicity that EF Core's InMemory test provider
/// does not reliably model for a mixed Added+Modified batch).
/// </summary>
public class SubmitInwardConcurrencyTests
{
    [Fact]
    public async Task PalletPositionRowVersion_ThrowsConcurrencyException_WhenTwoContextsRaceToSaveTheSamePosition()
    {
        // Direct proof of the underlying EF mechanism: two separate DbContext instances against
        // the same store, both load the position, one saves first and wins, the other's save
        // must fail rather than silently overwrite.
        var dbName = Guid.NewGuid().ToString();
        var seedDb = TestDbContextFactory.Create(dbName);
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 80 });
        seedDb.Racks.Add(rack);
        await seedDb.SaveChangesAsync();

        var contextA = TestDbContextFactory.Create(dbName);
        var contextB = TestDbContextFactory.Create(dbName);

        var positionA = await contextA.PalletPositions.FirstAsync(p => p.LocationCode == "A-01-01");
        var positionB = await contextB.PalletPositions.FirstAsync(p => p.LocationCode == "A-01-01");

        positionA.RowVersion++;
        await contextA.SaveChangesAsync(); // wins

        positionB.RowVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextB.SaveChangesAsync());
    }
}
