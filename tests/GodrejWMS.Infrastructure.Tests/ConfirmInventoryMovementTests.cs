using GodrejWMS.Application.Features.InventoryMovement.Commands;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using GodrejWMS.Application.Features.InventoryMovement.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers the Application-layer command/query the UI actually calls, on top of the
/// service-level tests in <see cref="InventoryMovementServiceTests"/>.</summary>
public class ConfirmInventoryMovementTests
{
    private static (AppDbContext Db, Material Material, PalletPosition Source, PalletPosition Destination, MovementReason Reason) SeedScenario()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var source = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        var destination = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-05-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(source);
        rack.PalletPositions.Add(destination);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000015, Description = "Product A", DesignType = "XOLDH", PackSize = 24,
            GrossWeightKg = 0.5m, LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);

        var reason = new MovementReason { Code = "SpaceOptimization", DisplayName = "Space Optimization", IsActive = true };
        db.MovementReasons.Add(reason);
        db.SaveChanges();

        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = source.Id, MfgMonth = 202603, QuantityBoxes = 25 });
        db.SaveChanges();

        return (db, material, source, destination, reason);
    }

    [Fact]
    public async Task ConfirmInventoryMovement_ProcessesMultipleLinesIndependently()
    {
        var (db, material, source, destination, reason) = SeedScenario();
        var handler = new ConfirmInventoryMovementHandler(db, new InventoryMovementService(db, new TestClock(), new TestCurrentUser()));

        var lines = new[]
        {
            new InventoryMovementLineInput(material.Id, 202603, source.Id, destination.Id, 20),
            new InventoryMovementLineInput(material.Id, 202603, source.Id, destination.Id, 999) // exceeds remaining 5
        };

        var results = await handler.Handle(new ConfirmInventoryMovementCommand(lines, reason.Id), CancellationToken.None);

        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal(20, db.StockBatches.Single(b => b.PalletPositionId == destination.Id).QuantityBoxes);
    }

    [Fact]
    public async Task ValidateInventoryMovementLines_ReturnsPerLineResults_WithoutPersisting()
    {
        var (db, material, source, destination, reason) = SeedScenario();
        var handler = new ValidateInventoryMovementLinesHandler(new InventoryMovementService(db, new TestClock(), new TestCurrentUser()));

        var lines = new[]
        {
            new InventoryMovementLineInput(material.Id, 202603, source.Id, destination.Id, 10),
            new InventoryMovementLineInput(material.Id, 202603, source.Id, source.Id, 10) // same as source
        };

        var results = await handler.Handle(new ValidateInventoryMovementLinesQuery(lines), CancellationToken.None);

        Assert.True(results[0].Valid);
        Assert.False(results[1].Valid);
        Assert.Contains("cannot be the same", results[1].Reason);
        Assert.Empty(db.StockMovements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ConfirmInventoryMovementValidator_RejectsMissingReason(int reasonId)
    {
        var db = TestDbContextFactory.Create();
        var validator = new ConfirmInventoryMovementValidator(db);

        var result = await validator.ValidateAsync(new ConfirmInventoryMovementCommand(
            [new InventoryMovementLineInput(1, 202603, 1, 2, 10)], reasonId));

        Assert.False(result.IsValid);
    }
}

file sealed class TestClock : GodrejWMS.Application.Common.Interfaces.IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

file sealed class TestCurrentUser : GodrejWMS.Application.Common.Interfaces.ICurrentUserService
{
    public string? UserId => "test-user";

    public string? UserName => "test@godrejwms.local";

    public bool IsInRole(string role) => true;
}
