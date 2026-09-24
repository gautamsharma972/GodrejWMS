using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutAllocationServiceTests
{
    [Fact]
    public async Task References_ContinueDailySequence_AndResetOnNextDate()
    {
        using var db = TestDbContextFactory.Create();
        db.PulloutTransactions.Add(new PulloutTransaction { ReferenceNumber = "PICK-20260916-162051" });
        await db.SaveChangesAsync();
        var clock = new PulloutClock { UtcNow = new(2026, 9, 16, 17, 0, 0, TimeSpan.Zero) };
        var handler = new GodrejWMS.Application.Features.Pullout.Commands.SubmitPulloutHandler(
            db, new PulloutAllocationService(db), clock, new PulloutUser());
        GodrejWMS.Application.Features.Pullout.Commands.SubmitPulloutLine[] lines = [new(123, 1)];

        var first = await handler.Handle(new(lines), default);
        var second = await handler.Handle(new(lines), default);
        Assert.Equal("PICK-20260916-162052", first.ReferenceNumber);
        Assert.Equal("PICK-20260916-162053", second.ReferenceNumber);
        var refreshed = await handler.Handle(new(lines, RefreshReference: first.ReferenceNumber), default);
        Assert.Equal(first.ReferenceNumber, refreshed.ReferenceNumber);

        clock.UtcNow = clock.UtcNow.AddDays(1);
        var nextDay = await handler.Handle(new(lines), default);
        Assert.Equal("PICK-20260917-000001", nextDay.ReferenceNumber);
    }

    [Fact]
    public async Task Preview_SavesPendingHistory_ConfirmationUpdatesInventoryOnce()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = new Material { MaterialNumber = 123, Description = "Test", DesignType = "X", SeasonId = SeasonIds.Rainy };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 10 });
        await db.SaveChangesAsync();
        var handler = new GodrejWMS.Application.Features.Pullout.Commands.SubmitPulloutHandler(
            db, new PulloutAllocationService(db), new PulloutClock(), new PulloutUser());
        GodrejWMS.Application.Features.Pullout.Commands.SubmitPulloutLine[] lines = [new(123, 3), new(123, 2)];
        var preview = await handler.Handle(new(lines), default);
        await db.SaveChangesAsync();
        Assert.Equal(10, db.StockBatches.Single().QuantityBoxes);
        Assert.False(Assert.Single(db.PulloutTransactions).IsConfirmed);
        Assert.Equal(5, Assert.Single(preview.Lines).PickedQuantityBoxes);

        db.ChangeTracker.Clear();
        var pending = await new GodrejWMS.Application.Features.Pullout.Queries.GetPulloutHistoryHandler(db)
            .Handle(new(IsConfirmed: false), default);
        Assert.Single(pending.Items);
        var detail = await new GodrejWMS.Application.Features.Pullout.Queries.GetPulloutDetailHandler(db)
            .Handle(new(pending.Items[0].Id), default);
        Assert.False(detail.IsConfirmed);
        Assert.Equal(preview.ReferenceNumber, detail.ReferenceNumber);

        var confirmed = await handler.Handle(new(lines, preview), default);
        Assert.Equal(preview.ReferenceNumber, confirmed.ReferenceNumber);
        Assert.Equal(5, db.StockBatches.Single().QuantityBoxes);
        Assert.Single(db.PulloutTransactions);
        Assert.True(db.PulloutTransactions.Single().IsConfirmed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new(lines, preview), default));
        Assert.Equal(5, db.StockBatches.Single().QuantityBoxes);

        var nextPreview = await handler.Handle(new(lines), default);
        db.StockBatches.Single().QuantityBoxes = 1;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new(lines, nextPreview), default));
        await db.SaveChangesAsync();
        Assert.Equal(1, db.StockBatches.Single().QuantityBoxes);
        Assert.Equal(2, db.PulloutTransactions.Count());
        var refreshed = await handler.Handle(new(lines, RefreshReference: nextPreview.ReferenceNumber), default);
        Assert.Equal(nextPreview.ReferenceNumber, refreshed.ReferenceNumber);
        Assert.Equal(1, Assert.Single(refreshed.Lines).PickedQuantityBoxes);
        Assert.Equal(1, db.StockBatches.Single().QuantityBoxes);
        Assert.Equal(2, db.PulloutTransactions.Count());
        await handler.Handle(new(lines, refreshed), default);
        Assert.Empty(db.StockBatches);
        Assert.All(db.PulloutTransactions, t => Assert.True(t.IsConfirmed));
    }

    [Fact]
    public async Task AllocateAsync_PicksOldestManufacturingMonthFirst_Fefo()
    {
        // Arrange: three batches of the same material in different pallet positions and mfg months.
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var pos1 = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        var pos2 = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01", CapacityBoxes = 40 };
        var pos3 = new PalletPosition { Column = 3, Level = 1, LocationCode = "A-03-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(pos1);
        rack.PalletPositions.Add(pos2);
        rack.PalletPositions.Add(pos3);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000015,
            Description = "A.PUR AEROSOL A.THR MRP 110 PS 24",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        // MAY batch sits first in a lower-numbered position, but MAR/APR are older and must be
        // drained first regardless of physical position order.
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = pos1.Id, MfgMonth = 201005, QuantityBoxes = 20 });
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = pos2.Id, MfgMonth = 201003, QuantityBoxes = 10 });
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = pos3.Id, MfgMonth = 201004, QuantityBoxes = 10 });
        await db.SaveChangesAsync();

        var sut = new PulloutAllocationService(db);

        // Act: pick 15 boxes — should fully drain MAR (10) then take 5 from APR, leaving MAY untouched.
        var result = await sut.AllocateAsync(material.Id, 15);
        await db.SaveChangesAsync(); // the service mutates tracked entities but leaves SaveChanges to the caller

        // Assert
        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(15, result.PickedQuantityBoxes);
        Assert.Equal(2, result.Picks.Count);

        Assert.Equal(201003, result.Picks[0].MfgMonth);
        Assert.Equal(10, result.Picks[0].QuantityBoxes);
        Assert.Equal(201004, result.Picks[1].MfgMonth);
        Assert.Equal(5, result.Picks[1].QuantityBoxes);

        // MAR batch was fully consumed (10 of 10) and removed; APR (partially consumed) and
        // MAY (untouched) remain — two batches left, not three.
        var remainingBatches = db.StockBatches.Where(b => b.MaterialId == material.Id).ToList();
        Assert.Equal(2, remainingBatches.Count);
    }

    [Fact]
    public async Task AllocateAsync_RemovesBatch_WhenFullyDepleted()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var pos = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(pos);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000015,
            Description = "Test",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = pos.Id, MfgMonth = 201003, QuantityBoxes = 10 });
        await db.SaveChangesAsync();

        var sut = new PulloutAllocationService(db);

        var result = await sut.AllocateAsync(material.Id, 10);
        await db.SaveChangesAsync();

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Empty(db.StockBatches.Where(b => b.MaterialId == material.Id));
    }

    [Fact]
    public async Task AllocateAsync_ReturnsFailed_WhenNoStockExists()
    {
        var db = TestDbContextFactory.Create();
        var material = new Material
        {
            MaterialNumber = 1,
            Description = "Test",
            DesignType = "X",
            PackSize = 1,
            GrossWeightKg = 1,
            LengthMm = 1,
            WidthMm = 1,
            HeightMm = 1,
            SeasonId = SeasonIds.Summer
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var sut = new PulloutAllocationService(db);
        var result = await sut.AllocateAsync(material.Id, 5);

        Assert.Equal(AllocationStatus.Failed, result.Status);
        Assert.Equal(0, result.PickedQuantityBoxes);
    }
}

file sealed class PulloutClock : GodrejWMS.Application.Common.Interfaces.IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

file sealed class PulloutUser : GodrejWMS.Application.Common.Interfaces.ICurrentUserService
{
    public string? UserId => "test";
    public string? UserName => "test";
    public bool IsInRole(string role) => true;
}
