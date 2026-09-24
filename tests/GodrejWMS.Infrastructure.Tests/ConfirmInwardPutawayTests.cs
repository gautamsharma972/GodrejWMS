using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>
/// Covers §25's requirement that confirmation re-validates current stock/capacity/material/status
/// at transaction time rather than trusting the original recommendation - the scenario two
/// supervisors racing to confirm put-aways into the same location must be caught by, not silently
/// overbook.
/// </summary>
public class ConfirmInwardPutawayTests
{
    private static (AppDbContext Db, Material Material, PalletPosition Position, InwardTransaction Transaction, InwardPutaway Putaway) SeedScenario(int capacityBoxes = 40)
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = capacityBoxes };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 1,
            Description = "Material A",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = capacityBoxes,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-CONFIRM-1" };
        var line = new InwardTransactionLine
        {
            InwardTransaction = transaction,
            MaterialId = material.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 20,
            AllocatedQuantityBoxes = 20,
            Status = AllocationStatus.Fulfilled
        };
        var putaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = position.Id,
            QuantityBoxes = 20,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(putaway);
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        db.SaveChanges();

        return (db, material, position, transaction, putaway);
    }

    [Fact]
    public async Task Confirm_CreatesStockBatch_AndIncrementsRowVersion_WhenLocationStillFree()
    {
        var (db, _, position, transaction, putaway) = SeedScenario();
        var handler = new ConfirmInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        await handler.Handle(new ConfirmInwardPutawayCommand(transaction.ReferenceNumber), CancellationToken.None);

        var confirmed = db.InwardPutaways.Single(p => p.Id == putaway.Id);
        Assert.True(confirmed.IsConfirmed);
        Assert.Equal(1u, confirmed.RowVersion);
        Assert.Equal("test@godrejwms.local", confirmed.ConfirmedByUserName);

        var batch = db.StockBatches.Single(b => b.PalletPositionId == position.Id);
        Assert.Equal(20, batch.QuantityBoxes);
        Assert.Equal(LocationSubtypeIds.Good, batch.StockSubtypeId);
    }

    [Fact]
    public async Task Confirm_Throws_WhenAnotherConfirmationAlreadyFilledTheLocation()
    {
        // Simulates the two-supervisors-same-location race: between this put-away's reservation
        // and this confirmation attempt, another confirmation already booked most of the capacity.
        var (db, material, position, transaction, _) = SeedScenario(capacityBoxes: 30);
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 201001, QuantityBoxes = 15 });
        await db.SaveChangesAsync();

        var handler = new ConfirmInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ConfirmInwardPutawayCommand(transaction.ReferenceNumber), CancellationToken.None));
        Assert.Contains("capacity", ex.Message);
    }

    [Fact]
    public async Task Confirm_Throws_WhenLocationNowHoldsADifferentMaterial()
    {
        var (db, _, position, transaction, _) = SeedScenario();
        var otherMaterial = new Material
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
        db.Materials.Add(otherMaterial);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = otherMaterial.Id, PalletPositionId = position.Id, MfgMonth = 201001, QuantityBoxes = 5 });
        await db.SaveChangesAsync();

        var handler = new ConfirmInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ConfirmInwardPutawayCommand(transaction.ReferenceNumber), CancellationToken.None));
        Assert.Contains("different material", ex.Message);
    }

    [Fact]
    public async Task Confirm_Throws_WhenLocationWasDeactivatedSinceReservation()
    {
        var (db, _, position, transaction, _) = SeedScenario();
        position.IsActive = false;
        await db.SaveChangesAsync();

        var handler = new ConfirmInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ConfirmInwardPutawayCommand(transaction.ReferenceNumber), CancellationToken.None));
        Assert.Contains("no longer active", ex.Message);
    }

    [Fact]
    public async Task Confirm_Throws_WhenMaterialsRequiredZoneChangedSinceReservation()
    {
        // The position was a valid target when this put-away was reserved, but the material's
        // required zone was tightened afterward (or the location's own zone was reassigned) -
        // confirmation must catch this, not just active/material/capacity.
        var (db, material, position, transaction, _) = SeedScenario();
        material.PreferredZoneTypeId = ZoneTypeIds.Fast; // position defaults to Reserve
        material.RequirePreferredZone = true;
        await db.SaveChangesAsync();

        var handler = new ConfirmInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ConfirmInwardPutawayCommand(transaction.ReferenceNumber), CancellationToken.None));
        Assert.Contains("required zone", ex.Message);
    }

    [Fact]
    public async Task ConfirmSingle_ConfirmsOnlyThatPutaway_LeavingOthersPending()
    {
        var (db, material, position, transaction, putaway) = SeedScenario();

        // A second, still-empty position with a second pending line, so the transaction as a
        // whole has two putaways - only one of which should be confirmed here.
        var position2 = new PalletPosition { RackId = position.RackId, Column = 2, Level = 1, LocationCode = "A-02-01", CapacityBoxes = 40 };
        db.PalletPositions.Add(position2);
        var line2 = new InwardTransactionLine
        {
            InwardTransactionId = transaction.Id,
            MaterialId = material.Id,
            MfgMonth = 201002,
            RequestedQuantityBoxes = 10,
            AllocatedQuantityBoxes = 10,
            Status = AllocationStatus.Fulfilled
        };
        var putaway2 = new InwardPutaway { InwardTransactionLine = line2, PalletPositionId = position2.Id, QuantityBoxes = 10 };
        line2.Putaways.Add(putaway2);
        db.InwardTransactionLines.Add(line2);
        await db.SaveChangesAsync();

        var handler = new ConfirmSingleInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        var result = await handler.Handle(new ConfirmSingleInwardPutawayCommand(putaway.Id), CancellationToken.None);

        Assert.False(result.IsConfirmed);
        Assert.True(db.InwardPutaways.Single(p => p.Id == putaway.Id).IsConfirmed);
        Assert.False(db.InwardPutaways.Single(p => p.Id == putaway2.Id).IsConfirmed);
        Assert.Single(db.StockBatches.Where(b => b.PalletPositionId == position.Id));
    }

    [Fact]
    public async Task ConfirmSingle_Throws_WhenAlreadyConfirmed()
    {
        var (db, _, _, _, putaway) = SeedScenario();
        putaway.IsConfirmed = true;
        await db.SaveChangesAsync();

        var handler = new ConfirmSingleInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ConfirmSingleInwardPutawayCommand(putaway.Id), CancellationToken.None));
    }
}

file sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

file sealed class TestCurrentUser : ICurrentUserService
{
    public string? UserId => "test-user";

    public string? UserName => "test@godrejwms.local";

    public bool IsInRole(string role) => true;
}
