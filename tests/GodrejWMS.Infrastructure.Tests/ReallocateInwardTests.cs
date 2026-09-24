using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>
/// Covers re-running the allocation engine for a GRN line that couldn't be fully placed when it
/// was first submitted (Partial/Failed), against warehouse availability as it stands now - e.g.
/// after another GRN's stale reservation was rejected, freeing up a location this one couldn't
/// have used before.
/// </summary>
public class ReallocateInwardTests
{
    private static (AppDbContext Db, Material Material, InwardTransaction Transaction, InwardTransactionLine Line) SeedPartialLine(
        int firstPositionCapacity = 20, decimal requested = 50, decimal alreadyAllocated = 20)
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = firstPositionCapacity };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 1, Description = "Material A", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-REALLOC-1" };
        var line = new InwardTransactionLine
        {
            InwardTransaction = transaction,
            MaterialId = material.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = requested,
            AllocatedQuantityBoxes = alreadyAllocated,
            Status = alreadyAllocated >= requested ? AllocationStatus.Fulfilled : AllocationStatus.Partial,
            Remarks = $"Warehouse at capacity - {requested - alreadyAllocated:0.###} box(es) could not be reserved."
        };
        if (alreadyAllocated > 0)
        {
            line.Putaways.Add(new InwardPutaway { PalletPositionId = position.Id, QuantityBoxes = alreadyAllocated, AllocationReason = "test seed" });
        }
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        db.SaveChanges();

        return (db, material, transaction, line);
    }

    [Fact]
    public async Task Reallocate_FullyClosesTheGap_WhenNewCapacityCoversAllOfIt()
    {
        var (db, _, transaction, line) = SeedPartialLine(requested: 50, alreadyAllocated: 20);

        // Simulates a location freeing up after another GRN was rejected.
        db.PalletPositions.Add(new PalletPosition { RackId = db.Racks.Single().Id, Column = 2, Level = 1, LocationCode = "A-02-01", CapacityBoxes = 40 });
        await db.SaveChangesAsync();

        var handler = new ReallocateInwardHandler(db, new PalletAllocationService(db, new TestClock()));
        var result = await handler.Handle(new ReallocateInwardCommand(transaction.Id), CancellationToken.None);

        var reloadedLine = db.InwardTransactionLines.Single(l => l.Id == line.Id);
        Assert.Equal(50, reloadedLine.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Fulfilled, reloadedLine.Status);
        Assert.Null(reloadedLine.Remarks);
        Assert.True(result.Lines.Single().IsConfirmed == false); // still just reserved, not confirmed

        var newPutaway = db.InwardPutaways.Single(p => p.PalletPosition.LocationCode == "A-02-01");
        Assert.Equal(30, newPutaway.QuantityBoxes);
        Assert.False(newPutaway.IsConfirmed);

        // The original reservation is untouched.
        Assert.Equal(20, db.InwardPutaways.Single(p => p.PalletPosition.LocationCode == "A-01-01").QuantityBoxes);
    }

    [Fact]
    public async Task Reallocate_PartiallyClosesTheGap_WhenNewCapacityIsNotEnough()
    {
        var (db, _, transaction, line) = SeedPartialLine(requested: 50, alreadyAllocated: 20);

        db.PalletPositions.Add(new PalletPosition { RackId = db.Racks.Single().Id, Column = 2, Level = 1, LocationCode = "A-02-01", CapacityBoxes = 10 });
        await db.SaveChangesAsync();

        var handler = new ReallocateInwardHandler(db, new PalletAllocationService(db, new TestClock()));
        await handler.Handle(new ReallocateInwardCommand(transaction.Id), CancellationToken.None);

        var reloadedLine = db.InwardTransactionLines.Single(l => l.Id == line.Id);
        Assert.Equal(30, reloadedLine.AllocatedQuantityBoxes); // 20 original + 10 newly placed
        Assert.Equal(AllocationStatus.Partial, reloadedLine.Status);
        Assert.NotNull(reloadedLine.Remarks);
    }

    [Fact]
    public async Task Reallocate_SkipsAlreadyFulfilledLines()
    {
        var (db, material, transaction, _) = SeedPartialLine(requested: 20, alreadyAllocated: 20); // Fulfilled

        var otherLine = new InwardTransactionLine
        {
            InwardTransactionId = transaction.Id,
            MaterialId = material.Id,
            MfgMonth = 201002,
            RequestedQuantityBoxes = 30,
            AllocatedQuantityBoxes = 0,
            Status = AllocationStatus.Failed,
            Remarks = "Warehouse at capacity"
        };
        db.InwardTransactionLines.Add(otherLine);
        db.PalletPositions.Add(new PalletPosition { RackId = db.Racks.Single().Id, Column = 2, Level = 1, LocationCode = "A-02-01", CapacityBoxes = 40 });
        await db.SaveChangesAsync();

        var handler = new ReallocateInwardHandler(db, new PalletAllocationService(db, new TestClock()));
        await handler.Handle(new ReallocateInwardCommand(transaction.Id), CancellationToken.None);

        var fulfilledLine = db.InwardTransactionLines.Single(l => l.MfgMonth == 201001);
        Assert.Equal(20, fulfilledLine.AllocatedQuantityBoxes); // untouched

        var reloadedOtherLine = db.InwardTransactionLines.Single(l => l.MfgMonth == 201002);
        Assert.Equal(30, reloadedOtherLine.AllocatedQuantityBoxes); // this is the one that got the new capacity
        Assert.Equal(AllocationStatus.Fulfilled, reloadedOtherLine.Status);
    }

    [Fact]
    public async Task Reallocate_Throws_WhenTransactionIsRejected()
    {
        var (db, _, transaction, _) = SeedPartialLine();
        transaction.IsRejected = true;
        await db.SaveChangesAsync();

        var handler = new ReallocateInwardHandler(db, new PalletAllocationService(db, new TestClock()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ReallocateInwardCommand(transaction.Id), CancellationToken.None));
        Assert.Contains("rejected", ex.Message);
    }

    [Fact]
    public async Task Reallocate_Throws_WhenEveryLineIsAlreadyFulfilled()
    {
        var (db, _, transaction, _) = SeedPartialLine(requested: 20, alreadyAllocated: 20); // Fulfilled
        var handler = new ReallocateInwardHandler(db, new PalletAllocationService(db, new TestClock()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ReallocateInwardCommand(transaction.Id), CancellationToken.None));
        Assert.Contains("already fully allocated", ex.Message);
    }
}

file sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
