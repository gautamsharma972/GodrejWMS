using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class MovementReportsTests
{
    private static (AppDbContext Db, PalletPosition PosA, PalletPosition PosB, PalletPosition PosC, MovementReason ReasonRack, MovementReason ReasonSpace) SeedScenario()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var posA = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        var posB = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01", CapacityBoxes = 40 };
        var posC = new PalletPosition { Column = 3, Level = 1, LocationCode = "A-03-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(posA);
        rack.PalletPositions.Add(posB);
        rack.PalletPositions.Add(posC);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 1, Description = "Material A", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);

        var reasonRack = new MovementReason { Code = "RackReorganization", DisplayName = "Rack Reorganization", SortOrder = 1, IsActive = true };
        var reasonSpace = new MovementReason { Code = "SpaceOptimization", DisplayName = "Space Optimization", SortOrder = 2, IsActive = true };
        db.MovementReasons.Add(reasonRack);
        db.MovementReasons.Add(reasonSpace);
        db.SaveChanges();

        // Alice moves A->B twice for RackReorganization; Bob moves B->C once for SpaceOptimization.
        db.StockMovements.Add(new StockMovement
        {
            MovementNumber = "IMV-1", MaterialId = material.Id, MfgMonth = 202601,
            SourcePalletPositionId = posA.Id, DestinationPalletPositionId = posB.Id,
            QuantityBoxes = 10, ReasonId = reasonRack.Id, PerformedByUserName = "alice",
            CreatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
        });
        db.StockMovements.Add(new StockMovement
        {
            MovementNumber = "IMV-2", MaterialId = material.Id, MfgMonth = 202601,
            SourcePalletPositionId = posA.Id, DestinationPalletPositionId = posB.Id,
            QuantityBoxes = 5, ReasonId = reasonRack.Id, PerformedByUserName = "alice",
            CreatedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero)
        });
        db.StockMovements.Add(new StockMovement
        {
            MovementNumber = "IMV-3", MaterialId = material.Id, MfgMonth = 202601,
            SourcePalletPositionId = posB.Id, DestinationPalletPositionId = posC.Id,
            QuantityBoxes = 20, ReasonId = reasonSpace.Id, PerformedByUserName = "bob",
            CreatedAt = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero)
        });
        // Outside the date-filter range used by the filtered tests below.
        db.StockMovements.Add(new StockMovement
        {
            MovementNumber = "IMV-OLD", MaterialId = material.Id, MfgMonth = 202512,
            SourcePalletPositionId = posC.Id, DestinationPalletPositionId = posA.Id,
            QuantityBoxes = 99, ReasonId = reasonSpace.Id, PerformedByUserName = "carol",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        db.SaveChanges();

        return (db, posA, posB, posC, reasonRack, reasonSpace);
    }

    [Fact]
    public async Task ReasonsBreakdown_GroupsByReason_WithCountAndQuantity()
    {
        var (db, _, _, _, reasonRack, reasonSpace) = SeedScenario();
        var handler = new GetMovementReasonsBreakdownHandler(db);

        var rows = await handler.Handle(new GetMovementReasonsBreakdownQuery(), CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var rackRow = Assert.Single(rows, r => r.ReasonId == reasonRack.Id);
        Assert.Equal(2, rackRow.MovementCount);
        Assert.Equal(15, rackRow.TotalQuantityBoxes);
        var spaceRow = Assert.Single(rows, r => r.ReasonId == reasonSpace.Id);
        Assert.Equal(2, spaceRow.MovementCount); // includes IMV-3 and IMV-OLD (no date filter applied here)
        Assert.Equal(119, spaceRow.TotalQuantityBoxes);
    }

    [Fact]
    public async Task ReasonsBreakdown_RespectsDateRange()
    {
        var (db, _, _, _, _, reasonSpace) = SeedScenario();
        var handler = new GetMovementReasonsBreakdownHandler(db);

        var rows = await handler.Handle(
            new GetMovementReasonsBreakdownQuery(FromDate: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        var spaceRow = Assert.Single(rows, r => r.ReasonId == reasonSpace.Id);
        Assert.Equal(1, spaceRow.MovementCount); // IMV-OLD (January) excluded
        Assert.Equal(20, spaceRow.TotalQuantityBoxes);
    }

    [Fact]
    public async Task ActivityByUser_GroupsByPerformedByUserName()
    {
        var (db, _, _, _, _, _) = SeedScenario();
        var handler = new GetMovementActivityByUserHandler(db);

        var rows = await handler.Handle(new GetMovementActivityByUserQuery(), CancellationToken.None);

        Assert.Equal(3, rows.Count); // alice, bob, carol
        var alice = Assert.Single(rows, r => r.UserName == "alice");
        Assert.Equal(2, alice.MovementCount);
        Assert.Equal(15, alice.TotalQuantityBoxes);
        var bob = Assert.Single(rows, r => r.UserName == "bob");
        Assert.Equal(1, bob.MovementCount);
        Assert.Equal(20, bob.TotalQuantityBoxes);
    }

    [Fact]
    public async Task LocationChurn_CombinesSourceAndDestinationCounts_PerLocation()
    {
        var (db, posA, posB, posC, _, _) = SeedScenario();
        var handler = new GetMovementLocationChurnHandler(db);

        var rows = await handler.Handle(new GetMovementLocationChurnQuery(), CancellationToken.None);

        // Position B: destination twice (IMV-1, IMV-2) + source once (IMV-3) = 3 total moves, the busiest location.
        var rowB = Assert.Single(rows, r => r.LocationCode == posB.LocationCode);
        Assert.Equal(1, rowB.MovesOut);
        Assert.Equal(2, rowB.MovesIn);
        Assert.Equal(3, rowB.TotalMoves);
        Assert.Equal(rowB.LocationCode, rows[0].LocationCode); // ordered by TotalMoves descending

        var rowA = Assert.Single(rows, r => r.LocationCode == posA.LocationCode);
        Assert.Equal(2, rowA.MovesOut); // IMV-1, IMV-2
        Assert.Equal(1, rowA.MovesIn); // IMV-OLD destination

        var rowC = Assert.Single(rows, r => r.LocationCode == posC.LocationCode);
        Assert.Equal(1, rowC.MovesOut); // IMV-OLD source
        Assert.Equal(1, rowC.MovesIn); // IMV-3 destination
    }
}
