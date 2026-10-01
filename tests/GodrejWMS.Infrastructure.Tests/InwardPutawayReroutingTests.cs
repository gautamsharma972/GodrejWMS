using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>
/// Covers the cross-SKU-mixing guard that both the manual reroute command and its candidate-list
/// query must enforce - a pallet position may only ever hold one material at a time, the same rule
/// <see cref="GodrejWMS.Infrastructure.Services.PalletAllocationService"/> already applies when it
/// picks a location automatically.
/// </summary>
public class InwardPutawayReroutingTests
{
    private static (AppDbContext Db, Material MaterialA, Material MaterialB, PalletPosition Current, PalletPosition Target, InwardPutaway Putaway) SeedScenario()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var current = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01" };
        var target = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01" };
        rack.PalletPositions.Add(current);
        rack.PalletPositions.Add(target);
        db.Racks.Add(rack);

        var materialA = new Material
        {
            MaterialNumber = 1,
            Description = "Material A",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        var materialB = new Material
        {
            MaterialNumber = 2,
            Description = "Material B",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.AddRange(materialA, materialB);
        db.SaveChanges();

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-TEST-1" };
        var line = new InwardTransactionLine
        {
            InwardTransaction = transaction,
            MaterialId = materialA.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 5,
            AllocatedQuantityBoxes = 5,
            Status = AllocationStatus.Fulfilled
        };
        var putaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = current.Id,
            QuantityBoxes = 5,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(putaway);
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        db.SaveChanges();

        return (db, materialA, materialB, current, target, putaway);
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenTargetHoldsConfirmedStockOfDifferentMaterial()
    {
        var (db, _, materialB, _, target, putaway) = SeedScenario();
        db.StockBatches.Add(new StockBatch { MaterialId = materialB.Id, PalletPositionId = target.Id, MfgMonth = 201001, QuantityBoxes = 5 });
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenTargetHasPendingReservationOfDifferentMaterial()
    {
        var (db, _, materialB, _, target, putaway) = SeedScenario();
        var otherTransaction = new InwardTransaction { ReferenceNumber = "GRN-TEST-2" };
        var otherLine = new InwardTransactionLine
        {
            InwardTransaction = otherTransaction,
            MaterialId = materialB.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 5,
            AllocatedQuantityBoxes = 5,
            Status = AllocationStatus.Fulfilled
        };
        otherLine.Putaways.Add(new InwardPutaway { InwardTransactionLine = otherLine, PalletPositionId = target.Id, QuantityBoxes = 5 });
        otherTransaction.Lines.Add(otherLine);
        db.InwardTransactions.Add(otherTransaction);
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeLocation_Succeeds_AndRecordsOverrideReason_WhenTargetIsFree()
    {
        var (db, _, _, _, target, putaway) = SeedScenario();
        var handler = new ChangeInwardPutawayLocationHandler(db);

        await handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id, "Damaged pallet in original slot"), CancellationToken.None);

        var moved = db.InwardPutaways.Single(p => p.Id == putaway.Id);
        Assert.Equal(target.Id, moved.PalletPositionId);
        Assert.Equal("Damaged pallet in original slot", moved.OverrideReason);
        Assert.Equal(1u, moved.RowVersion);
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenTargetIsOutsideRequiredZone()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        materialA.PreferredZoneTypeId = ZoneTypeIds.Fast;
        materialA.RequirePreferredZone = true;
        // target keeps its default ZoneTypeId (Reserve), which is outside the required zone.
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id, "Test reason"), CancellationToken.None));
        Assert.Contains("required zone", ex.Message);
    }

    [Fact]
    public async Task GetChangeOptions_ExcludesPositionsOutsideRequiredZone()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        materialA.PreferredZoneTypeId = ZoneTypeIds.Fast;
        materialA.RequirePreferredZone = true;
        await db.SaveChangesAsync();

        var handler = new GetInwardPalletChangeOptionsHandler(db);

        var options = await handler.Handle(new GetInwardPalletChangeOptionsQuery(putaway.Id), CancellationToken.None);

        Assert.DoesNotContain(options, o => o.Id == target.Id);
    }

    [Fact]
    public async Task GetChangeOptions_IncludesPositionsInsideRequiredZone()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        materialA.PreferredZoneTypeId = ZoneTypeIds.Reserve; // matches target's (default) zone
        materialA.RequirePreferredZone = true;
        await db.SaveChangesAsync();

        var handler = new GetInwardPalletChangeOptionsHandler(db);

        var options = await handler.Handle(new GetInwardPalletChangeOptionsQuery(putaway.Id), CancellationToken.None);

        Assert.Contains(options, o => o.Id == target.Id);
    }

    [Fact]
    public async Task GetChangeOptions_ExcludesPositionsHeldByDifferentMaterial()
    {
        var (db, _, materialB, _, target, putaway) = SeedScenario();
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        db.StockBatches.Add(new StockBatch { MaterialId = materialB.Id, PalletPositionId = target.Id, MfgMonth = 201001, QuantityBoxes = 5 });
        await db.SaveChangesAsync();

        var handler = new GetInwardPalletChangeOptionsHandler(db);

        var options = await handler.Handle(new GetInwardPalletChangeOptionsQuery(putaway.Id), CancellationToken.None);

        Assert.DoesNotContain(options, o => o.Id == target.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validator_Rejects_MissingOrBlankReason(string? reason)
    {
        var validator = new ChangeInwardPutawayLocationValidator();

        var result = await validator.ValidateAsync(new ChangeInwardPutawayLocationCommand(1, 2, reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangeInwardPutawayLocationCommand.Reason));
    }

    [Fact]
    public async Task Validator_Accepts_NonBlankReason()
    {
        var validator = new ChangeInwardPutawayLocationValidator();

        var result = await validator.ValidateAsync(new ChangeInwardPutawayLocationCommand(1, 2, "Damaged pallet"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ChangeRack_MovesAllSelectedReservations_InOneRequest()
    {
        var (db, _, _, _, _, firstPutaway) = SeedScenario();
        var destinationRack = new Rack { Code = "B", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        destinationRack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "B-01-01" });
        destinationRack.PalletPositions.Add(new PalletPosition { Column = 2, Level = 1, LocationCode = "B-02-01" });
        db.Racks.Add(destinationRack);

        var line = firstPutaway.InwardTransactionLine;
        var secondPutaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = firstPutaway.PalletPositionId,
            QuantityBoxes = 5,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(secondPutaway);
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayRackHandler(db);
        await handler.Handle(
            new ChangeInwardPutawayRackCommand([firstPutaway.Id, secondPutaway.Id], "B", "Use Rack B"),
            CancellationToken.None);

        var destinationPositionIds = destinationRack.PalletPositions.Select(p => p.Id).ToHashSet();
        var moved = db.InwardPutaways.Where(p => p.Id == firstPutaway.Id || p.Id == secondPutaway.Id).ToList();
        Assert.All(moved, p => Assert.Contains(p.PalletPositionId, destinationPositionIds));
        Assert.All(moved, p => Assert.Equal("Use Rack B", p.OverrideReason));
    }

    [Fact]
    public async Task ChangeRack_MakesNoChanges_WhenRackCannotFitCompleteSelection()
    {
        var (db, material, _, _, _, firstPutaway) = SeedScenario();
        firstPutaway.QuantityBoxes = 50;
        var destinationRack = new Rack { Code = "B", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        destinationRack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "B-01-01", MaxPallets = 2 });
        db.Racks.Add(destinationRack);

        var line = firstPutaway.InwardTransactionLine;
        var secondPutaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = firstPutaway.PalletPositionId,
            QuantityBoxes = 50,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(secondPutaway);
        await db.SaveChangesAsync();
        var originalPositionId = firstPutaway.PalletPositionId;

        var handler = new ChangeInwardPutawayRackHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangeInwardPutawayRackCommand([firstPutaway.Id, secondPutaway.Id], "B", "Use Rack B"),
            CancellationToken.None));

        Assert.Equal(originalPositionId, firstPutaway.PalletPositionId);
        Assert.Equal(originalPositionId, secondPutaway.PalletPositionId);
        Assert.Null(firstPutaway.OverrideReason);
        Assert.Null(secondPutaway.OverrideReason);
        Assert.Equal(40, material.PalletCapacityBoxes);
    }
}
