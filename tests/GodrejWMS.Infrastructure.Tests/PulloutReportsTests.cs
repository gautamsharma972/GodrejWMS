using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutReportsTests
{
    private static (AppDbContext Db, PalletPosition Position, Material Material) SeedWarehouse()
    {
        var db = TestDbContextFactory.Create();

        // PalletPosition defaults to LocationTypeId=Rack(1)/ZoneTypeId=Reserve(2); seed matching
        // master rows so the Location Utilization report's required ZoneType/LocationType
        // navigations resolve (EF Core translates that join to an inner join, so an unresolved
        // FK silently drops the row from the result rather than throwing).
        db.LocationTypes.Add(new LocationType { Id = LocationTypeIds.Rack, Code = "Rack", DisplayName = "Rack" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });

        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 1, Description = "Material A", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        return (db, position, material);
    }

    [Fact]
    public async Task PendingPullout_IsExcludedFromCompletedPickReports()
    {
        var (db, position, material) = SeedWarehouse();
        var transaction = new PulloutTransaction { ReferenceNumber = "PENDING", IsConfirmed = false };
        var line = new PulloutTransactionLine { MaterialId = material.Id, RequestedQuantityBoxes = 15, PickedQuantityBoxes = 10, Status = AllocationStatus.Partial };
        line.Picks.Add(new PulloutPick { PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 10 });
        transaction.Lines.Add(line);
        db.PulloutTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var age = await new GetPulloutStockAgeAtPickHandler(db).Handle(new(), default);
        Assert.Equal(0, age.PickCount);
        Assert.Empty(await new GetPulloutLocationUtilizationHandler(db).Handle(new(), default));
        Assert.Empty(await new GetPulloutExceptionsHandler(db).Handle(new(), default));
    }

    [Fact]
    public async Task StockAgeAtPick_ComputesAgeInDays_FromMfgMonthToPickDate()
    {
        var (db, position, material) = SeedWarehouse();
        var pickedAt = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

        var transaction = new PulloutTransaction { ReferenceNumber = "PUL-1", CreatedAt = pickedAt };
        var line = new PulloutTransactionLine { MaterialId = material.Id, RequestedQuantityBoxes = 10, PickedQuantityBoxes = 10, Status = AllocationStatus.Fulfilled };
        // MfgMonth = August 2026 (202608), pick date = 15 Sep 2026 -> age = 15 days (Aug 1 to Sep 15... let's just assert it's positive and consistent).
        line.Picks.Add(new PulloutPick { PalletPositionId = position.Id, MfgMonth = 202608, QuantityBoxes = 10 });
        transaction.Lines.Add(line);
        db.PulloutTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var handler = new GetPulloutStockAgeAtPickHandler(db);
        var summary = await handler.Handle(new GetPulloutStockAgeAtPickQuery(), CancellationToken.None);

        Assert.Equal(1, summary.PickCount);
        var row = Assert.Single(summary.Rows);
        Assert.Equal("PUL-1", row.ReferenceNumber);
        Assert.Equal("A-01-01", row.LocationCode);
        Assert.Equal(45, row.AgeDays); // 1 Aug -> 15 Sep = 45 days
        Assert.Equal(45, summary.AverageAgeDays);
    }

    [Fact]
    public async Task LocationUtilization_GroupsPicksByZoneAndLocationType()
    {
        var (db, position, material) = SeedWarehouse();

        var transaction = new PulloutTransaction { ReferenceNumber = "PUL-2" };
        var line = new PulloutTransactionLine { MaterialId = material.Id, RequestedQuantityBoxes = 15, PickedQuantityBoxes = 15, Status = AllocationStatus.Fulfilled };
        line.Picks.Add(new PulloutPick { PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 15 });
        transaction.Lines.Add(line);
        db.PulloutTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var handler = new GetPulloutLocationUtilizationHandler(db);
        var rows = await handler.Handle(new GetPulloutLocationUtilizationQuery(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.PickCount);
        Assert.Equal(15, row.TotalQuantityBoxes);
    }

    [Fact]
    public async Task Exceptions_ReturnsOnlyPartialOrFailedLines_WithShortfall()
    {
        var (db, _, material) = SeedWarehouse();

        var transaction = new PulloutTransaction { ReferenceNumber = "PUL-3" };
        transaction.Lines.Add(new PulloutTransactionLine { MaterialId = material.Id, RequestedQuantityBoxes = 40, PickedQuantityBoxes = 40, Status = AllocationStatus.Fulfilled });
        transaction.Lines.Add(new PulloutTransactionLine { MaterialId = material.Id, RequestedQuantityBoxes = 30, PickedQuantityBoxes = 5, Status = AllocationStatus.Partial, Remarks = "Insufficient stock" });
        db.PulloutTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var handler = new GetPulloutExceptionsHandler(db);
        var rows = await handler.Handle(new GetPulloutExceptionsQuery(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Partial", row.Status);
        Assert.Equal(25, row.ShortfallBoxes);
    }
}
