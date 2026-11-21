using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.CycleCount;
using GodrejWMS.Application.Features.CycleCount.Commands;
using GodrejWMS.Application.Features.CycleCount.Queries;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Cycle Count scheduling, schedule management and the Inward / Pullout freeze.</summary>
public class CycleCountScheduleTests
{
    private sealed class AllWarehouses : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(warehouseId == 1);
    }

    private sealed class Notes : IRealtimeNotificationService
    {
        public List<RealtimeNotification> Published { get; } = [];
        public IDisposable Subscribe(string? userId, IReadOnlyCollection<string> roles, Func<RealtimeNotification, Task> handler) => new Noop();
        public Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default) { Published.Add(notification); return Task.CompletedTask; }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);   // before "now" (2026-11-08)
    private static readonly DateTimeOffset End = new(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        db.Warehouses.Add(new Warehouse { Id = 2, Code = "GDR2", Name = "Godrej 2" });
        foreach (var (code, wh) in new[] { ("A", 1), ("B", 1), ("C", 2) })
        {
            var rack = new Rack { WarehouseId = wh, Code = code, Columns = 2, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
            for (var c = 1; c <= 2; c++) rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"{code}-{c:00}-01", MaxPallets = 1 });
            db.Racks.Add(rack);
        }

        db.Materials.Add(new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static ScheduleCycleCountHandler Schedule(AppDbContext db, Notes? notes = null) =>
        new(db, new AllWarehouses(), new SvcClock(), new SvcUser(), notes ?? new Notes());

    private static Task<CycleCountBatchDto> ScheduleAsync(AppDbContext db, string[] racks, DateTimeOffset? from = null, bool ack = true) =>
        Schedule(db).Handle(new ScheduleCycleCountCommand(1, from ?? Start, End, racks, "test", ack), default);

    [Fact]
    public async Task Schedule_GeneratesUniqueSequentialBatchNumbers_AndStoresTheRacks()
    {
        using var db = await SeedAsync();

        var first = await ScheduleAsync(db, ["A"]);
        var second = await ScheduleAsync(db, ["b"]);

        Assert.Equal("CC-20261108-000001", first.BatchNumber);
        Assert.Equal("CC-20261108-000002", second.BatchNumber);
        Assert.Equal(CycleCountBatchStatus.Scheduled, first.Status);
        Assert.Equal(["B"], second.Racks.Select(r => r.RackCode)); // stored with the rack's own spelling
        Assert.Contains(db.CycleCountHistory, h => h.Action == CycleCountActions.Scheduled && h.CycleCountBatchId == first.Id);
    }

    [Fact]
    public async Task Schedule_RefusesARackOfAnotherWarehouse_AnUnknownRack_AndAnAlreadyOpenRack()
    {
        using var db = await SeedAsync();
        await ScheduleAsync(db, ["A"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ScheduleAsync(db, ["C"]));      // belongs to warehouse 2
        await Assert.ThrowsAsync<InvalidOperationException>(() => ScheduleAsync(db, ["ZZ"]));
        var clash = await Assert.ThrowsAsync<InvalidOperationException>(() => ScheduleAsync(db, ["A", "B"]));
        Assert.Contains("already part of the open cycle count", clash.Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Schedule(db).Handle(new ScheduleCycleCountCommand(2, Start, End, ["C"]), default));  // warehouse not assigned
    }

    [Fact]
    public async Task Schedule_WarnsAboutOrdersInFlight_AndNeedsAcknowledgement()
    {
        using var db = await SeedAsync();
        db.PulloutTransactions.Add(new PulloutTransaction { WarehouseId = 1, ReferenceNumber = "PICK-1", IsConfirmed = true, ConfirmedAt = null });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ScheduleAsync(db, ["A"], ack: false));
        Assert.Contains("pullout", ex.Message);
        Assert.Empty(db.CycleCountBatches);
        Assert.NotNull(await ScheduleAsync(db, ["A"], ack: true));
    }

    [Fact]
    public async Task OpenBatch_BlocksNewInwardAndPullout_ForItsWarehouseOnly_AndLiftsOnDropOrClose()
    {
        using var db = await SeedAsync();
        var batch = await ScheduleAsync(db, ["A"]);

        var inward = await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitInward(db));
        Assert.Contains(batch.BatchNumber, inward.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SubmitInwardWithoutPkmHandler(db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(40000001, 5)], WarehouseId: 1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitPullout(db));
        Assert.True((await new GetCycleCountFreezeHandler(db, new SvcClock(), new AllWarehouses()).Handle(new GetCycleCountFreezeQuery(1), default)).IsFrozen);

        await new DropCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new DropCycleCountCommand(batch.Id, "not needed"), default);
        Assert.False((await new GetCycleCountFreezeHandler(db, new SvcClock(), new AllWarehouses()).Handle(new GetCycleCountFreezeQuery(1), default)).IsFrozen);
        await SubmitInward(db); // allowed again
    }

    [Fact]
    public async Task FreezeThatAutoReleases_EndsAfterScheduleTo_ButTheDefaultStaysUntilClosed()
    {
        using var db = await SeedAsync();
        var past = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero); // "now" is 2026-11-08, so the window is over
        var auto = await Schedule(db).Handle(new ScheduleCycleCountCommand(1, Start, past, ["A"], null, true, AutoReleaseFreezeAtScheduleEnd: true), default);
        Assert.True(auto.AutoReleaseFreezeAtScheduleEnd);
        await SubmitInward(db); // window over and the batch asked to release: allowed

        var manual = await Schedule(db).Handle(new ScheduleCycleCountCommand(1, Start, past, ["B"], null, true), default);
        Assert.False(manual.AutoReleaseFreezeAtScheduleEnd);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitInward(db)); // default: frozen until closed or dropped
    }

    [Fact]
    public async Task ScheduledForTheFuture_DoesNotFreezeYet_AndAnotherOpenBatchKeepsTheFreeze()
    {
        using var db = await SeedAsync();
        var future = await ScheduleAsync(db, ["A"], from: new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        await SubmitInward(db); // window has not started

        await new DropCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes()).Handle(new DropCycleCountCommand(future.Id, "x"), default);
        var one = await ScheduleAsync(db, ["A"]);
        var two = await ScheduleAsync(db, ["B"]);
        await new DropCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes()).Handle(new DropCycleCountCommand(one.Id, "x"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitInward(db)); // batch two still open
        Assert.Equal(CycleCountBatchStatus.Scheduled, db.CycleCountBatches.Single(b => b.Id == two.Id).Status);
    }

    [Fact]
    public async Task Drop_NeedsAReason_KeepsTheBatchAndItsHistory()
    {
        using var db = await SeedAsync();
        var batch = await ScheduleAsync(db, ["A"]);

        Assert.False(new DropCycleCountValidator().Validate(new DropCycleCountCommand(batch.Id, " ")).IsValid);
        var dropped = await new DropCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new DropCycleCountCommand(batch.Id, "stock audit moved"), default);

        Assert.Equal(CycleCountBatchStatus.Dropped, dropped.Status);
        Assert.Equal("stock audit moved", dropped.DropReason);
        Assert.Equal(2, db.CycleCountHistory.Count(h => h.CycleCountBatchId == batch.Id)); // scheduled + dropped
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DropCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes()).Handle(new DropCycleCountCommand(batch.Id, "again"), default));
        // the rack is free for a new batch
        Assert.NotNull(await ScheduleAsync(db, ["A"]));
    }

    [Fact]
    public async Task Extend_And_Reduce_RecordPreviousAndNewSchedule_AndNeverDiscardPendingCards()
    {
        using var db = await SeedAsync();
        var batch = await ScheduleAsync(db, ["A"]);

        var extended = await new ExtendCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ExtendCycleCountCommand(batch.Id, End.AddDays(2), "more time"), default);
        Assert.Equal(CycleCountBatchStatus.Extended, extended.Status);
        var history = db.CycleCountHistory.Single(h => h.Action == CycleCountActions.Extended);
        Assert.Contains("2026-11-30", history.PreviousValue);
        Assert.Contains("2026-12-02", history.NewValue);
        Assert.Equal("more time", history.Reason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ExtendCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ExtendCycleCountCommand(batch.Id, End, "earlier"), default)); // not later than the current end

        // a pending card: reducing needs an acknowledgement and keeps the card
        var loaded = db.CycleCountBatches.Single(b => b.Id == batch.Id);
        db.CycleCountCards.Add(new CycleCountCard
        {
            CycleCountBatchId = loaded.Id, RackCode = "A", PalletPositionId = db.PalletPositions.First().Id, SequenceKey = "A|0001|001|000000000000"
        });
        loaded.CardsGenerated = true;
        await db.SaveChangesAsync();
        var reduce = new ReduceCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes());
        var warn = await Assert.ThrowsAsync<InvalidOperationException>(() => reduce.Handle(new ReduceCycleCountCommand(batch.Id, End, "less", false), default));
        Assert.Contains("pending", warn.Message);
        var reduced = await reduce.Handle(new ReduceCycleCountCommand(batch.Id, End, "less", true), default);
        Assert.Equal(End, reduced.ScheduleTo);
        Assert.Equal(1, db.CycleCountCards.Count());
    }

    [Fact]
    public async Task PartialClose_ExcludesTheUncountedCards_KeepsCompletedOnes_AndIsClosableOnlyWhenNoRackIsOpen()
    {
        using var db = await SeedAsync();
        var batch = await ScheduleAsync(db, ["A", "B"]);
        var entity = db.CycleCountBatches.Include(b => b.Racks).Single(b => b.Id == batch.Id);
        entity.Status = CycleCountBatchStatus.InProgress;
        entity.CardsGenerated = true;
        var posA = db.PalletPositions.First(p => p.LocationCode == "A-01-01").Id;
        var posB = db.PalletPositions.First(p => p.LocationCode == "B-01-01").Id;
        db.CycleCountCards.Add(new CycleCountCard { CycleCountBatchId = entity.Id, RackCode = "A", PalletPositionId = posA, SequenceKey = "A", State = CycleCountCardState.Verified });
        db.CycleCountCards.Add(new CycleCountCard { CycleCountBatchId = entity.Id, RackCode = "A", PalletPositionId = posA, SequenceKey = "A2" });
        db.CycleCountCards.Add(new CycleCountCard { CycleCountBatchId = entity.Id, RackCode = "B", PalletPositionId = posB, SequenceKey = "B" });
        await db.SaveChangesAsync();

        var partial = await new PartiallyCloseCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new PartiallyCloseCycleCountCommand(batch.Id, "ran out of time"), default);

        Assert.Equal(CycleCountBatchStatus.PartiallyClosed, partial.Status);
        Assert.Equal(2, db.CycleCountCards.Count(c => c.IsExcluded));
        Assert.Equal(CycleCountRackStatus.Open, db.CycleCountBatchRacks.Single(r => r.RackCode == "A").Status);       // has a counted card: still submittable
        Assert.Equal(CycleCountRackStatus.Excluded, db.CycleCountBatchRacks.Single(r => r.RackCode == "B").Status);  // nothing counted
        Assert.Equal(100m, partial.CompletionPercent);                                                                  // 1 of 1 remaining card
        var notReady = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CloseCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes()).Handle(new CloseCycleCountCommand(batch.Id), default));
        Assert.Contains("A", notReady.Message);
    }

    [Fact]
    public async Task OtherWarehouseBatch_CannotBeReadOrChangedByItsId()
    {
        using var db = await SeedAsync();
        var other = new CycleCountBatch { BatchNumber = "CC-20261108-000099", WarehouseId = 2, ScheduleFrom = Start, ScheduleTo = End };
        db.CycleCountBatches.Add(other);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<GodrejWMS.Application.Common.Exceptions.NotFoundException>(() =>
            new GetCycleCountBatchDetailHandler(db, new AllWarehouses()).Handle(new GetCycleCountBatchDetailQuery(other.Id), default));
        await Assert.ThrowsAsync<GodrejWMS.Application.Common.Exceptions.NotFoundException>(() =>
            new DropCycleCountHandler(db, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes()).Handle(new DropCycleCountCommand(other.Id, "x"), default));
        // the batch number sequence is global even for a warehouse the caller cannot see
        var next = await CycleCountRules.NextBatchNumberAsync(db, TestServices.Now, default);
        Assert.Equal("CC-20261108-000100", next);
    }

    [Fact]
    public async Task ManageActions_WorkOnTheCurrentBatch_EvenWhenThisSessionLoadedAnOlderCopy()
    {
        var name = Guid.NewGuid().ToString();
        using var web = TestDbContextFactory.Create(name);     // the browser session: one long-lived context
        using var phone = TestDbContextFactory.Create(name);   // a counter on another device
        web.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 });
        web.Racks.Add(rack);
        await web.SaveChangesAsync();
        var batch = await Schedule(web).Handle(new ScheduleCycleCountCommand(1, Start, End, ["A"], null, true), default);

        // the web session reads the batch (and keeps it tracked) ...
        await new GetCycleCountBatchDetailHandler(web, new AllWarehouses()).Handle(new GetCycleCountBatchDetailQuery(batch.Id), default);
        await new ExtendCycleCountHandler(web, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ExtendCycleCountCommand(batch.Id, End.AddDays(1), "first"), default);
        // ... then a counter on the phone changes it (the batch row version moves on)
        var changed = phone.CycleCountBatches.Single();
        changed.RowVersion += 5;
        changed.Status = CycleCountBatchStatus.InProgress;
        phone.CycleCountBatchRacks.Single().RowVersion += 3;
        await phone.SaveChangesAsync();

        // extending again from the older copy must work on the current row, not fail with a concurrency error
        var extended = await new ExtendCycleCountHandler(web, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ExtendCycleCountCommand(batch.Id, End.AddDays(2), "second"), default);
        Assert.Equal(End.AddDays(2), extended.ScheduleTo);
        Assert.Equal(2, phone.CycleCountHistory.Count(h => h.Action == CycleCountActions.Extended));

        var changedAgain = phone.CycleCountBatches.Single();
        phone.Entry(changedAgain).Reload();
        changedAgain.RowVersion += 5;
        await phone.SaveChangesAsync();
        var reduced = await new ReduceCycleCountHandler(web, new AllWarehouses(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ReduceCycleCountCommand(batch.Id, End.AddDays(1), "third", true), default);
        Assert.Equal(End.AddDays(1), reduced.ScheduleTo);
    }

    private static Task SubmitInward(AppDbContext db) =>
        new SubmitInwardHandler(db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(40000001, 5, "SEP|2026")], WarehouseId: 1), default);

    private static Task SubmitPullout(AppDbContext db) =>
        new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, 1)], WarehouseId: 1), default);
}
