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
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = NewMaterial(1, mrpPrice: 10m, packSize: 5);
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 4 });
        await db.SaveChangesAsync();

        var handler = new GetInventoryValuationHandler(db, new FakePermissionService());
        var rows = await handler.Handle(new GetInventoryValuationQuery(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(4, row.TotalBoxes);
        Assert.Equal(200, row.TotalValue); // 4 boxes * 5 pack size * 10 MRP
    }

    [Fact]
    public async Task Valuation_AllowsOperator()
    {
        var db = SeedWarehouseMasters();
        var handler = new GetInventoryValuationHandler(db, new FakePermissionService());

        var rows = await handler.Handle(new GetInventoryValuationQuery(), CancellationToken.None);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task MaterialStockReport_GroupsByMaterialAndPkm_AndAppliesQuantityFilters()
    {
        var db = SeedWarehouseMasters();
        var rack = new Rack { Code = "A", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var firstPosition = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
        var secondPosition = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01", MaxPallets = 1 };
        rack.PalletPositions.Add(firstPosition);
        rack.PalletPositions.Add(secondPosition);
        db.Racks.Add(rack);
        var material = NewMaterial(40019311, mrpPrice: 125.50m);
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        db.StockBatches.AddRange(
            new StockBatch { MaterialId = material.Id, PalletPositionId = firstPosition.Id, MfgMonth = 202601, QuantityBoxes = 4 },
            new StockBatch { MaterialId = material.Id, PalletPositionId = secondPosition.Id, MfgMonth = 202601, QuantityBoxes = 7 },
            new StockBatch { MaterialId = material.Id, PalletPositionId = secondPosition.Id, MfgMonth = 202602, QuantityBoxes = 2 });
        await db.SaveChangesAsync();

        var handler = new GetMaterialStockReportHandler(db, new FakeWarehouseAccessService(), new FakePermissionService());
        var rows = await handler.Handle(new GetMaterialStockReportQuery(new MaterialStockReportFilter(
            Search: "40019311", PkmFrom: 202601, PkmTo: 202601, MinMrp: 100m, MaxMrp: 150m,
            MinBoxes: 10m, MaxBoxes: 12m)), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(40019311, row.MaterialCode);
        Assert.Equal(202601, row.Pkm);
        Assert.Equal(125.50m, row.Mrp);
        Assert.Equal(11, row.BoxQuantity);
    }

    [Fact]
    public async Task Aging_BucketsStockByAge_AndCrossReferencesVelocity()
    {
        var db = SeedWarehouseMasters();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
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
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
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

    private sealed class FakePermissionService : GodrejWMS.Application.Common.Permissions.IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 9, 16);
    }

    private sealed class FakeWarehouseAccessService : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([]);

        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
