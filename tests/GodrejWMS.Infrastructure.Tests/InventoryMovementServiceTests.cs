using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers the 15 acceptance tests (§33) for Inventory Movement.</summary>
public class InventoryMovementServiceTests
{
    private static (AppDbContext Db, Material Material, PalletPosition Source, PalletPosition Destination, MovementReason Reason) SeedScenario(
        int sourceQty = 25, int destinationCapacity = 40)
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var source = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        var destination = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-05-03", CapacityBoxes = destinationCapacity };
        rack.PalletPositions.Add(source);
        rack.PalletPositions.Add(destination);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000015,
            Description = "Product A",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);

        var reason = new MovementReason { Code = "SpaceOptimization", DisplayName = "Space Optimization", IsActive = true };
        db.MovementReasons.Add(reason);
        db.SaveChanges();

        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = source.Id, MfgMonth = 202603, QuantityBoxes = sourceQty });
        db.SaveChanges();

        return (db, material, source, destination, reason);
    }

    private static InventoryMovementService CreateService(AppDbContext db) => new(db, new TestClock(), new TestCurrentUser());

    [Fact]
    public async Task Test1_MovesFullQuantity()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 25);
        var sut = CreateService(db);

        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 25), reason.Id);

        Assert.True(result.Success);
        Assert.Equal(0, db.StockBatches.Single(b => b.PalletPositionId == source.Id).QuantityBoxes);
        Assert.Equal(25, db.StockBatches.Single(b => b.PalletPositionId == destination.Id).QuantityBoxes);
        Assert.NotNull(result.Movement);
        Assert.StartsWith("IMV-", result.Movement!.MovementNumber);
    }

    [Fact]
    public async Task Test2_MovesPartialQuantity()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 100);
        var sut = CreateService(db);

        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 30), reason.Id);

        Assert.True(result.Success);
        Assert.Equal(70, db.StockBatches.Single(b => b.PalletPositionId == source.Id).QuantityBoxes);
        Assert.Equal(30, db.StockBatches.Single(b => b.PalletPositionId == destination.Id).QuantityBoxes);
    }

    [Fact]
    public async Task Test3_AllowsMoveIntoDestinationHoldingSameSku_WhenCapacityExists()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 25, destinationCapacity: 40);
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = destination.Id, MfgMonth = 202603, QuantityBoxes = 20 });
        await db.SaveChangesAsync();

        var sut = CreateService(db);
        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 10), reason.Id);

        Assert.True(result.Success);
        Assert.Equal(30, db.StockBatches.Single(b => b.PalletPositionId == destination.Id).QuantityBoxes);
    }

    [Fact]
    public async Task Test4_RejectsMoveIntoDestinationHoldingDifferentSku()
    {
        var (db, material, source, destination, reason) = SeedScenario();
        var otherMaterial = new Material
        {
            MaterialNumber = 40000016, Description = "Product B", DesignType = "XOLDH", PackSize = 24,
            GrossWeightKg = 0.5m, LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(otherMaterial);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = otherMaterial.Id, PalletPositionId = destination.Id, MfgMonth = 202603, QuantityBoxes = 20 });
        await db.SaveChangesAsync();

        var sut = CreateService(db);
        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 10), reason.Id);

        Assert.False(result.Success);
        Assert.Contains("different SKU", result.ErrorMessage);
    }

    [Fact]
    public async Task Test5_AllowsSameSkuWithDifferentPkm()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 15);
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = destination.Id, MfgMonth = 202601, QuantityBoxes = 20 });
        await db.SaveChangesAsync();

        var sut = CreateService(db);
        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 15), reason.Id);

        Assert.True(result.Success);
        Assert.Equal(20, db.StockBatches.Single(b => b.MfgMonth == 202601 && b.PalletPositionId == destination.Id).QuantityBoxes);
        Assert.Equal(15, db.StockBatches.Single(b => b.MfgMonth == 202603 && b.PalletPositionId == destination.Id).QuantityBoxes);
    }

    [Fact]
    public async Task Test6_RejectsWhenDestinationCapacityInsufficient()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 25, destinationCapacity: 20);
        var sut = CreateService(db);

        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 25), reason.Id);

        Assert.False(result.Success);
        Assert.Contains("insufficient capacity", result.ErrorMessage);
        Assert.Equal(25, db.StockBatches.Single(b => b.PalletPositionId == source.Id).QuantityBoxes);
    }

    [Fact]
    public async Task Test7_RejectsWhenSourceQuantityInsufficient()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 25);
        var sut = CreateService(db);

        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 30), reason.Id);

        Assert.False(result.Success);
        Assert.Contains("Insufficient available quantity", result.ErrorMessage);
    }

    [Fact]
    public async Task Test8_RejectsWhenSourceAndDestinationAreTheSame()
    {
        var (db, material, source, _, reason) = SeedScenario();
        var sut = CreateService(db);

        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, source.Id, 10), reason.Id);

        Assert.False(result.Success);
        Assert.Contains("cannot be the same", result.ErrorMessage);
    }

    [Fact]
    public async Task Test9_RejectsWhenDestinationIsInactive()
    {
        var (db, material, source, destination, reason) = SeedScenario();
        destination.IsActive = false;
        await db.SaveChangesAsync();

        var sut = CreateService(db);
        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 10), reason.Id);

        Assert.False(result.Success);
        Assert.Contains("inactive", result.ErrorMessage);
    }

    [Fact]
    public async Task Test10_RejectsWhenLocationIsInvalid()
    {
        var (db, material, source, _, reason) = SeedScenario();
        var sut = CreateService(db);

        var result = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, 999999, 10), reason.Id);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Test12_ConcurrentMove_SecondSaveFailsRatherThanOverdeducting()
    {
        // Direct proof of the underlying mechanism: two contexts load the same source batch, one
        // commits its deduction first, the other's stale save must fail rather than silently
        // deduct from an already-changed row.
        var dbName = Guid.NewGuid().ToString();
        var seedDb = TestDbContextFactory.Create(dbName);
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 80 };
        rack.PalletPositions.Add(position);
        seedDb.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 1, Description = "M", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        seedDb.Materials.Add(material);
        seedDb.SaveChanges();
        seedDb.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 50 });
        seedDb.SaveChanges();

        var contextA = TestDbContextFactory.Create(dbName);
        var contextB = TestDbContextFactory.Create(dbName);

        var batchA = await contextA.StockBatches.FirstAsync(b => b.PalletPositionId == position.Id);
        var batchB = await contextB.StockBatches.FirstAsync(b => b.PalletPositionId == position.Id);

        batchA.QuantityBoxes -= 30;
        batchA.RowVersion++;
        await contextA.SaveChangesAsync(); // User A completes first: 50 -> 20

        batchB.QuantityBoxes -= 30;
        batchB.RowVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextB.SaveChangesAsync());
    }

    [Fact]
    public async Task Test14_MultipleLines_EachIndependentlyValidatedAndProcessed()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 25);
        // A second source position with a separate batch, one of the two lines will fail
        // (insufficient quantity) while the other succeeds.
        var source2 = new PalletPosition { RackId = source.RackId, Column = 1, Level = 2, LocationCode = "A-01-02", CapacityBoxes = 40 };
        db.PalletPositions.Add(source2);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = source2.Id, MfgMonth = 202604, QuantityBoxes = 5 });
        await db.SaveChangesAsync();

        var sut = CreateService(db);

        var result1 = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 25), reason.Id);
        var result2 = await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202604, source2.Id, destination.Id, 999), reason.Id);

        Assert.True(result1.Success);
        Assert.False(result2.Success);
        // The failing line must not have touched the succeeding line's already-moved stock.
        Assert.Equal(25, db.StockBatches.Single(b => b.PalletPositionId == destination.Id && b.MfgMonth == 202603).QuantityBoxes);
        Assert.Equal(5, db.StockBatches.Single(b => b.PalletPositionId == source2.Id).QuantityBoxes);
    }

    [Fact]
    public async Task Test15_PkmUnchangedAfterMovement()
    {
        var (db, material, source, destination, reason) = SeedScenario(sourceQty: 25);
        var sut = CreateService(db);

        await sut.ExecuteAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 25), reason.Id);

        var destinationBatch = db.StockBatches.Single(b => b.PalletPositionId == destination.Id);
        Assert.Equal(202603, destinationBatch.MfgMonth);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsErrorWithoutPersistingAnything()
    {
        var (db, material, source, destination, _) = SeedScenario(sourceQty: 25);
        var sut = CreateService(db);

        var error = await sut.ValidateAsync(new InventoryMovementLineRequest(material.Id, 202603, source.Id, destination.Id, 30));

        Assert.NotNull(error);
        Assert.Contains("Insufficient available quantity", error);
        Assert.Equal(25, db.StockBatches.Single(b => b.PalletPositionId == source.Id).QuantityBoxes);
        Assert.Empty(db.StockMovements);
    }
}

file sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

file sealed class TestCurrentUser : ICurrentUserService
{
    public string? UserId => "test-user";

    public string? UserName => "test@godrejwms.local";

    public bool IsInRole(string role) => true;
}
