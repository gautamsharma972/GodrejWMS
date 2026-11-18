using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class AddPulloutPickTests
{
    private static async Task<(AppDbContext Db, List<PalletPosition> Positions, Material M1, Material M2)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 4, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 4; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        Material New(long number) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        var m1 = New(40000001);
        var m2 = New(40000002);
        db.Materials.AddRange(m1, m2);
        await db.SaveChangesAsync();
        var positions = rack.PalletPositions.OrderBy(p => p.Column).ToList();
        // m1: older PKM at A-01, newer PKM at A-02; m2: stock at A-03.
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = m1.Id, PalletPositionId = positions[0].Id, MfgMonth = 202609, QuantityBoxes = 20 },
            new StockBatch { MaterialId = m1.Id, PalletPositionId = positions[1].Id, MfgMonth = 202610, QuantityBoxes = 30 },
            new StockBatch { MaterialId = m2.Id, PalletPositionId = positions[2].Id, MfgMonth = 202610, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(m1.MaterialNumber, 10)], WarehouseId: 1), default);
        return (db, positions, m1, m2);
    }

    private static AddPulloutPickHandler Handler(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock());

    private static decimal OnHand(AppDbContext db, PalletPosition p, int materialId) =>
        db.StockBatches.AsNoTracking().Where(b => b.PalletPositionId == p.Id && b.MaterialId == materialId).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task SupervisorAddsAnotherMaterial_FromAChosenLocation_DeductsAtOnce_AndTheNewPickStillNeedsSubmitting()
    {
        var (db, positions, _, m2) = await SeedAsync();
        using var _ = db;
        var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;

        var options = await new GetPulloutAddOptionsHandler(db).Handle(new GetPulloutAddOptionsQuery(pulloutId, m2.Id), default);
        Assert.Equal(positions[2].Id, Assert.Single(options).PalletPositionId);

        var result = await Handler(db).Handle(new AddPulloutPickCommand(pulloutId, m2.Id, positions[2].Id, 15), default);

        Assert.Equal(15, result.AddedBoxes);
        Assert.False(result.BreaksFifo);
        Assert.Equal(25, OnHand(db, positions[2], m2.Id)); // deducted at once
        var line = db.PulloutTransactionLines.AsNoTracking().Single(l => l.MaterialId == m2.Id);
        Assert.Equal(15, line.RequestedQuantityBoxes);
        Assert.Equal(15, line.PickedQuantityBoxes);
        var pick = db.PulloutPicks.AsNoTracking().Single(p => p.PulloutTransactionLineId == line.Id);
        Assert.Null(pick.PickedAt); // still to be submitted
        Assert.Contains(db.StockLedgerEntries.AsNoTracking(), e => e.EntryType == StockLedgerEntryTypes.PulloutDeduct && e.QuantityBoxes == -15);
    }

    [Fact]
    public async Task AddingMoreOfAMaterialAlreadyOnThePullout_GrowsItsLine()
    {
        var (db, positions, m1, _) = await SeedAsync();
        using var _ = db;
        var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;
        var before = db.PulloutTransactionLines.AsNoTracking().Single(l => l.MaterialId == m1.Id).RequestedQuantityBoxes;

        // The pullout of 10 took the oldest PKM at A-01; A-01 still holds 10 of it.
        await Handler(db).Handle(new AddPulloutPickCommand(pulloutId, m1.Id, positions[0].Id, 5), default);

        var line = db.PulloutTransactionLines.AsNoTracking().Single(l => l.MaterialId == m1.Id);
        Assert.Equal(before + 5, line.RequestedQuantityBoxes);
        Assert.Equal(2, db.PulloutPicks.AsNoTracking().Count(p => p.PulloutTransactionLineId == line.Id));
    }

    [Fact]
    public async Task PickingANewerPkmWhileOlderStockExists_NeedsAReason()
    {
        var (db, positions, m1, _) = await SeedAsync();
        using var _ = db;
        var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new AddPulloutPickCommand(pulloutId, m1.Id, positions[1].Id, 5), default));
        Assert.Contains("FIFO", ex.Message);
        Assert.Equal(30, OnHand(db, positions[1], m1.Id));

        var ok = await Handler(db).Handle(new AddPulloutPickCommand(pulloutId, m1.Id, positions[1].Id, 5, "Customer asked for this lot"), default);
        Assert.True(ok.BreaksFifo);
        Assert.Equal(25, OnHand(db, positions[1], m1.Id));
    }

    [Fact]
    public async Task CannotAddMoreThanTheLocationHolds_OrOnAConfirmedPickList()
    {
        var (db, positions, _, m2) = await SeedAsync();
        using var _ = db;
        var pullout = await db.PulloutTransactions.SingleAsync();

        var tooMany = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new AddPulloutPickCommand(pullout.Id, m2.Id, positions[2].Id, 41), default));
        Assert.Contains("holds 40", tooMany.Message);

        // A failed command clears the change tracker, so load the pullout again before confirming it.
        pullout = await db.PulloutTransactions.SingleAsync();
        pullout.ConfirmedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var closed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new AddPulloutPickCommand(pullout.Id, m2.Id, positions[2].Id, 5), default));
        Assert.Contains("open", closed.Message);
    }
}
