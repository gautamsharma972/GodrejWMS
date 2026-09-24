using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class InventoryReportsTests
{
    private static AppDbContext SeedWarehouseMasters()
    {
        var db = TestDbContextFactory.Create();
        db.LocationTypes.Add(new LocationType { Id = LocationTypeIds.Rack, Code = "Rack", DisplayName = "Rack" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        db.SkuMovementTypes.Add(new SkuMovementType { Id = SkuMovementTypeIds.FastMoving, Code = "Fast", DisplayName = "Fast Moving" });
        db.SkuMovementTypes.Add(new SkuMovementType { Id = SkuMovementTypeIds.SlowMoving, Code = "Slow", DisplayName = "Slow Moving" });
        // SeasonIds.Rainy is 0, which EF Core's int-PK convention treats as "unset" and
        // auto-generates over - explicitly seeding Summer(1) instead so the id actually sticks.
        db.Seasons.Add(new Season { Id = SeasonIds.Summer, Code = "Summer", DisplayName = "Summer" });
        db.SaveChanges();
        return db;
    }

    private static Material NewMaterial(int number, decimal mrpPrice, int packSize = 24, int movementTypeId = SkuMovementTypeIds.FastMoving) => new()
    {
        MaterialNumber = number, Description = $"Material {number}", DesignType = "XOLDH", PackSize = packSize,
        GrossWeightKg = 0.5m, LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40,
        SeasonId = SeasonIds.Summer, MrpPrice = mrpPrice, MovementTypeId = movementTypeId
    };

    [Fact]
    public async Task Valuation_ComputesTotalValue_AsBoxesTimesPackSizeTimesMrp()
    {
        var db = SeedWarehouseMasters();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = NewMaterial(1, mrpPrice: 10m, packSize: 5);
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 4 });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser(isAdmin: true);
        var handler = new GetInventoryValuationHandler(db, currentUser);
        var rows = await handler.Handle(new GetInventoryValuationQuery(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(4, row.TotalBoxes);
        Assert.Equal(200, row.TotalValue); // 4 boxes * 5 pack size * 10 MRP
    }

    [Fact]
    public async Task Valuation_Throws_WhenCallerIsNotAdmin()
    {
        var db = SeedWarehouseMasters();
        var currentUser = new FakeCurrentUser(isAdmin: false);
        var handler = new GetInventoryValuationHandler(db, currentUser);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new GetInventoryValuationQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Aging_BucketsStockByAge_AndCrossReferencesVelocity()
    {
        var db = SeedWarehouseMasters();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);

        var slowMover = NewMaterial(1, mrpPrice: 5m, movementTypeId: SkuMovementTypeIds.SlowMoving);
        db.Materials.Add(slowMover);
        await db.SaveChangesAsync();

        // "Today" is fixed at 2026-09-16 by FakeClock; MfgMonth 202601 (1 Jan 2026) is well over 90 days old.
        db.StockBatches.Add(new StockBatch { MaterialId = slowMover.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 12 });
        await db.SaveChangesAsync();

        var handler = new GetInventoryAgingHandler(db, new FakeClock());
        var rows = await handler.Handle(new GetInventoryAgingQuery(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("90+ days", row.AgeBucket);
        Assert.Equal("Slow Moving", row.MovementTypeName);
        Assert.Equal(12, row.TotalBoxes);
    }

    [Fact]
    public async Task WarehouseUtilization_ComputesOccupancyPercent_ByZoneAndLocationType()
    {
        var db = SeedWarehouseMasters();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = NewMaterial(1, mrpPrice: 1m);
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 20 });
        await db.SaveChangesAsync();

        var handler = new GetWarehouseUtilizationByZoneHandler(db);
        var result = await handler.Handle(new GetWarehouseUtilizationByZoneQuery(), CancellationToken.None);

        var zoneRow = Assert.Single(result.ByZone);
        Assert.Equal(20, zoneRow.OccupiedBoxes);
        Assert.Equal(40, zoneRow.CapacityBoxes);
        Assert.Equal(50, zoneRow.OccupancyPercent);

        var typeRow = Assert.Single(result.ByLocationType);
        Assert.Equal(50, typeRow.OccupancyPercent);
    }

    private sealed class FakeCurrentUser(bool isAdmin) : ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? UserName => "test@godrejwms.local";
        public bool IsInRole(string role) => role == "Admin" && isAdmin;
    }

    private sealed class FakeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 9, 16);
    }
}
