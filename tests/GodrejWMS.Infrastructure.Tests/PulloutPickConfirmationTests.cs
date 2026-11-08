using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutPickConfirmationTests
{
    private sealed record Setup(AppDbContext Db, Material Material, PalletPosition TopLevel, PalletPosition Ground, PalletPosition Other, string Reference);

    /// <summary>A-01-01 (the "level 6" bin) holds 50 boxes; a pullout of 45 has been uploaded, so 45 are deducted and 5 remain.
    /// A-02-01 is an empty ground bin and A-03-01 holds 60 boxes of the same material for correction tests.</summary>
    private static async Task<Setup> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 3; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        var positions = rack.PalletPositions.OrderBy(p => p.Column).ToList();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = positions[0].Id, MfgMonth = 202610, QuantityBoxes = 50 });
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = positions[2].Id, MfgMonth = 202610, QuantityBoxes = 60 });
        await db.SaveChangesAsync();

        var submit = new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());
        // Allocation is FIFO by rack order, so the 45 come from A-01-01.
        var result = await submit.Handle(new SubmitPulloutCommand([new SubmitPulloutLine(material.MaterialNumber, 45)], WarehouseId: 1), default);
        return new Setup(db, material, positions[0], positions[1], positions[2], result.ReferenceNumber);
    }

    private static ConfirmPulloutPickHandler Handler(AppDbContext db) =>
        new(db, new PickAllow(), new SvcClock(), new SvcUser());

    private static ConfirmPulloutPicklistHandler Picklist(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new PickAllow(), new SvcClock(), new SvcUser());

    private static Task<ConfirmPulloutPicklistResult> ConfirmPicklistAsync(AppDbContext db) =>
        Picklist(db).Handle(new ConfirmPulloutPicklistCommand(db.PulloutTransactions.AsNoTracking().Single().Id), default);

    private static decimal OnHand(AppDbContext db, PalletPosition position) =>
        db.StockBatches.AsNoTracking().Where(b => b.PalletPositionId == position.Id).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task PickingTheAllocatedQuantity_ConfirmsThePick_AndLeavesTheRestOnTheShelf()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pick = s.Db.PulloutPicks.Single();

        var result = await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(pick.Id, 45), default);

        Assert.Equal(45, result.PickedBoxes);
        Assert.Equal(5, result.RemainingAtSourceBoxes);
        var saved = s.Db.PulloutPicks.AsNoTracking().Single();
        Assert.NotNull(saved.PickedAt);
        Assert.Equal(45, saved.PickedQuantityBoxes);
        Assert.Equal(5, OnHand(s.Db, s.TopLevel));
    }

    [Fact]
    public async Task RemainderMove_IsOnlyChosenAtPickTime_AndCarriedOutWhenThePickListIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pick = s.Db.PulloutPicks.Single();

        var result = await Handler(s.Db).Handle(
            new ConfirmPulloutPickCommand(pick.Id, 45, new PulloutRemainderMove(s.Ground.Id, 5)), default);

        // The choice is recorded, nothing has moved yet.
        Assert.Equal(5, result.MovedBoxes);
        Assert.Equal("A-02-01", result.DestinationLocationCode);
        Assert.Equal(5, OnHand(s.Db, s.TopLevel));
        Assert.Equal(0, OnHand(s.Db, s.Ground));
        Assert.Empty(s.Db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.PickRelocate));
        var saved = s.Db.PulloutPicks.AsNoTracking().Single();
        Assert.Equal(s.Ground.Id, saved.MoveToPalletPositionId);
        Assert.Equal(5, saved.MoveQuantityBoxes);

        var confirmed = await ConfirmPicklistAsync(s.Db);

        Assert.Equal(5, confirmed.MovedBoxes);
        Assert.Equal(0, OnHand(s.Db, s.TopLevel));
        Assert.Equal(5, OnHand(s.Db, s.Ground));
        var relocation = s.Db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.PickRelocate).OrderBy(e => e.Id).ToList();
        Assert.Equal(new decimal[] { -5, 5 }, relocation.Select(e => e.QuantityBoxes).ToArray());
    }

    [Fact]
    public async Task NoMoveChosen_LeavesTheRemainderWhereItIs_EvenAfterThePickListIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(s.Db.PulloutPicks.Single().Id, 45), default);
        await ConfirmPicklistAsync(s.Db);

        Assert.Equal(5, OnHand(s.Db, s.TopLevel));
        Assert.Equal(0, OnHand(s.Db, s.Ground));
        Assert.Empty(s.Db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.PickRelocate));
    }

    [Fact]
    public async Task PicksCanBeEditedAgain_UntilThePickListIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pickId = s.Db.PulloutPicks.Single().Id;

        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(pickId, 45, new PulloutRemainderMove(s.Ground.Id, 5)), default);
        // Changed their mind: fewer boxes found and no move after all.
        var again = await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(pickId, 40), default);

        Assert.Equal(5, again.ReturnedToStockBoxes);
        var edited = s.Db.PulloutPicks.AsNoTracking().Single();
        Assert.Equal(40, edited.PickedQuantityBoxes);
        Assert.Null(edited.MoveToPalletPositionId);

        await ConfirmPicklistAsync(s.Db);

        // After the pick-list is confirmed the picks are final.
        var locked = await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(s.Db).Handle(new ConfirmPulloutPickCommand(pickId, 45), default));
        Assert.Contains("already been confirmed", locked.Message);
    }

    [Fact]
    public async Task RemainderCanMoveIntoAVirtualLocation_WhichBecomesInUseWhenThePickListIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var virtuals = TestServices.Virtuals(s.Db);
        var spare = await virtuals.AcquireAsync(1, null);
        await virtuals.ReleaseIfEmptyAsync(spare);
        await s.Db.SaveChangesAsync();
        Assert.Equal(VirtualLocationStatus.Free, spare.VirtualStatus);

        await Handler(s.Db).Handle(
            new ConfirmPulloutPickCommand(s.Db.PulloutPicks.Single().Id, 45, new PulloutRemainderMove(spare.Id, 5)), default);
        Assert.Equal(0, OnHand(s.Db, spare));

        await ConfirmPicklistAsync(s.Db);

        Assert.Equal(5, OnHand(s.Db, spare));
        Assert.Equal(VirtualLocationStatus.InUse, s.Db.PalletPositions.AsNoTracking().Single(p => p.Id == spare.Id).VirtualStatus);
    }

    [Fact]
    public async Task RemainderThatExceedsThePalletSize_IsRejected_AndNothingIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        // A-03-01 already holds 60 boxes of the material - more than one 50-box pallet - so even 5 more cannot go there.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(s.Db).Handle(
            new ConfirmPulloutPickCommand(s.Db.PulloutPicks.Single().Id, 45, new PulloutRemainderMove(s.Other.Id, 5)), default));

        Assert.Contains("cannot take 5", ex.Message);
        Assert.Null(s.Db.PulloutPicks.AsNoTracking().Single().PickedAt);
        Assert.Equal(5, OnHand(s.Db, s.TopLevel));
        Assert.Equal(60, OnHand(s.Db, s.Other));
    }

    [Fact]
    public async Task InvalidDestinationsAndQuantities_AreRejected()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pickId = s.Db.PulloutPicks.Single().Id;

        async Task<string> Fail(ConfirmPulloutPickCommand command) =>
            (await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(s.Db).Handle(command, default))).Message;

        Assert.Contains("different location", await Fail(new(pickId, 45, new PulloutRemainderMove(s.TopLevel.Id, 5))));
        Assert.Contains("not found", await Fail(new(pickId, 45, new PulloutRemainderMove(99999, 5))));
        Assert.Contains("Only 5 box(es) remain", await Fail(new(pickId, 45, new PulloutRemainderMove(s.Ground.Id, 6))));
        // (a failed command clears the context, so the position is read again before it is changed)
        (await s.Db.PalletPositions.FirstAsync(p => p.Id == s.Ground.Id)).IsActive = false;
        await s.Db.SaveChangesAsync();
        Assert.Contains("not active", await Fail(new(pickId, 45, new PulloutRemainderMove(s.Ground.Id, 5))));
        Assert.Contains("Only 45", await Fail(new(pickId, 46)));
        Assert.Null(s.Db.PulloutPicks.AsNoTracking().Single().PickedAt);
    }

    [Fact]
    public async Task ShortPick_ReturnsTheMissingBoxesToStock_WhenThePickListIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        var result = await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(s.Db.PulloutPicks.Single().Id, 40), default);

        Assert.Equal(5, result.ReturnedToStockBoxes);
        Assert.Equal(10, result.RemainingAtSourceBoxes); // 5 never taken + 5 that will be handed back
        // Not yet: the pick can still be edited, so stock and the line are untouched.
        Assert.Equal(5, OnHand(s.Db, s.TopLevel));
        Assert.Equal(45, s.Db.PulloutTransactionLines.AsNoTracking().Single().PickedQuantityBoxes);

        await ConfirmPicklistAsync(s.Db);

        Assert.Equal(10, OnHand(s.Db, s.TopLevel));
        var line = s.Db.PulloutTransactionLines.AsNoTracking().Single();
        Assert.Equal(40, line.PickedQuantityBoxes);
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Partial, line.Status);
        Assert.Equal(40, s.Db.PulloutPicks.AsNoTracking().Single().QuantityBoxes);
    }

    [Fact]
    public async Task RemainderQueryAndSuggestions_PointToGroundLevelAndSameSkuLocations()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pickId = s.Db.PulloutPicks.AsNoTracking().Single().Id;

        var remainder = await new GetPickRemainderHandler(s.Db).Handle(new GetPickRemainderQuery(pickId), default);
        Assert.Equal(5, remainder.RemainingBoxes);
        Assert.Equal(45, remainder.PlannedBoxes);
        Assert.False(remainder.IsPicked);

        // A-03-01 holds 60 boxes (over a 50-box pallet), so it has no room; the empty ground bin is offered first.
        var suggestions = await new GetRelocationSuggestionsHandler(s.Db).Handle(new GetRelocationSuggestionsQuery(pickId, 5), default);
        var best = Assert.Single(suggestions);
        Assert.Equal("A-02-01", best.LocationCode);
        Assert.Equal(1, best.Level);
        Assert.Contains("ground", best.Reason);
    }

    [Fact]
    public async Task PickerPickList_ListsPostedPulloutsWithUnconfirmedPicks_UntilEveryPickIsConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var list = new GetPickerPendingPulloutsHandler(s.Db, new PickAllow());

        var pending = Assert.Single((await list.Handle(new GetPickerPendingPulloutsQuery(), default)).Items);
        Assert.Equal(s.Reference, pending.ReferenceNumber);
        Assert.Equal(0, pending.PickedCount);
        Assert.Equal(1, pending.PickCount);

        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(s.Db.PulloutPicks.AsNoTracking().Single().Id, 45), default);

        // Every pick is confirmed, but the pick-list stays on the picker's list until they press Confirm on it.
        Assert.Single((await list.Handle(new GetPickerPendingPulloutsQuery(), default)).Items);

        await new ConfirmPulloutPicklistHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new PickAllow(), new SvcClock(), new SvcUser())
            .Handle(new ConfirmPulloutPicklistCommand(s.Db.PulloutTransactions.AsNoTracking().Single().Id), default);
        Assert.Empty((await list.Handle(new GetPickerPendingPulloutsQuery(), default)).Items);
    }

    [Fact]
    public async Task ConfirmingThePickList_NeedsEveryPickConfirmed_ThenSetsThePickReferenceToConfirmed()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pulloutId = s.Db.PulloutTransactions.AsNoTracking().Single().Id;
        var confirm = new ConfirmPulloutPicklistHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new PickAllow(), new SvcClock(), new SvcUser());
        var history = new GetPulloutHistoryHandler(s.Db);

        // Posted on upload: not confirmed until the picker says so.
        Assert.False((await new GetPulloutDetailHandler(s.Db).Handle(new GetPulloutDetailQuery(pulloutId), default)).PickingConfirmed);
        Assert.False(Assert.Single((await history.Handle(new GetPulloutHistoryQuery(), default)).Items).IsConfirmed);

        var early = await Assert.ThrowsAsync<InvalidOperationException>(() => confirm.Handle(new ConfirmPulloutPicklistCommand(pulloutId), default));
        Assert.Contains("Confirm every pick first", early.Message);

        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(s.Db.PulloutPicks.AsNoTracking().Single().Id, 45), default);
        var result = await confirm.Handle(new ConfirmPulloutPicklistCommand(pulloutId), default);

        Assert.Equal(1, result.PickCount);
        var detail = await new GetPulloutDetailHandler(s.Db).Handle(new GetPulloutDetailQuery(pulloutId), default);
        Assert.True(detail.PickingConfirmed);
        Assert.NotNull(detail.PickingConfirmedByUserName);
        Assert.True(Assert.Single((await history.Handle(new GetPulloutHistoryQuery(), default)).Items).IsConfirmed);

        // Confirmed means shipped: it can no longer be confirmed again or rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => confirm.Handle(new ConfirmPulloutPicklistCommand(pulloutId), default));
        var reject = new RejectPulloutHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock(), new SvcUser());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reject.Handle(new RejectPulloutCommand(detail.ReferenceNumber, "Too late"), default));
    }

    [Fact]
    public async Task OverridingFifo_NeedsAReason_ButKeepingFifoDoesNot()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        // Newer stock (NOV|2026) sits in the ground bin; the allocated pick is the oldest PKM (OCT|2026).
        s.Db.StockBatches.Add(new StockBatch { MaterialId = s.Material.Id, PalletPositionId = s.Ground.Id, MfgMonth = 202611, QuantityBoxes = 50 });
        await s.Db.SaveChangesAsync();
        var change = new ChangePulloutPickLocationHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db));
        ChangePulloutPickLocationCommand ToNewer(string reason) => new(
            s.Reference, s.Material.MaterialNumber, "A-01-01", "OCT|2026", [new PulloutPickSource(s.Ground.Id, "NOV|2026", 45)], reason);

        var options = await new GetPulloutPickChangeOptionsHandler(s.Db).Handle(
            new GetPulloutPickChangeOptionsQuery(s.Reference, s.Material.MaterialNumber, "A-01-01", "OCT|2026"), default);
        Assert.True(options.Single(o => o.LocationCode == "A-02-01").BreaksFifo);
        Assert.False(options.Single(o => o.LocationCode == "A-03-01").BreaksFifo);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => change.Handle(ToNewer(""), default));
        Assert.Contains("overrides FIFO", refused.Message);
        Assert.Equal(45, s.Db.PulloutPicks.AsNoTracking().Single().QuantityBoxes);
        Assert.Equal(5, OnHand(s.Db, s.TopLevel)); // nothing moved

        await change.Handle(ToNewer("Older stock is damaged"), default);
        var pick = s.Db.PulloutPicks.AsNoTracking().Single();
        Assert.Equal(s.Ground.Id, pick.PalletPositionId);
        Assert.Equal("Older stock is damaged", pick.OverrideReason);

        // Keeping FIFO (another location of the oldest PKM) needs no reason at all.
        var freeChange = await change.Handle(new ChangePulloutPickLocationCommand(
            s.Reference, s.Material.MaterialNumber, "A-02-01", "NOV|2026", [new PulloutPickSource(s.Other.Id, "OCT|2026", 45)], ""), default);
        Assert.Equal(s.Other.Id, s.Db.PulloutPicks.AsNoTracking().Single().PalletPositionId);
        Assert.Null(s.Db.PulloutPicks.AsNoTracking().Single().OverrideReason);
    }

    [Fact]
    public async Task ChangingThePickLocation_OfAPostedPullout_HandsBackTheOldSourceAndTakesFromTheNewOne()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var change = new ChangePulloutPickLocationHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db));

        await change.Handle(new ChangePulloutPickLocationCommand(
            s.Reference, s.Material.MaterialNumber, "A-01-01", "OCT|2026",
            [new PulloutPickSource(s.Other.Id, "OCT|2026", 45)], "Closer to dispatch"), default);

        Assert.Equal(50, OnHand(s.Db, s.TopLevel)); // the 45 are back
        Assert.Equal(15, OnHand(s.Db, s.Other));
        var pick = s.Db.PulloutPicks.AsNoTracking().Single();
        Assert.Equal(s.Other.Id, pick.PalletPositionId);
        Assert.Equal("Closer to dispatch", pick.OverrideReason);
        Assert.Contains(s.Db.StockLedgerEntries.AsNoTracking(), e => e.EntryType == StockLedgerEntryTypes.PickChange && e.QuantityBoxes == 45);

        // A confirmed pick can still be re-pointed until the pick-list is confirmed (it comes back unconfirmed); after that it is final.
        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(pick.Id, 45), default);
        await change.Handle(new ChangePulloutPickLocationCommand(
            s.Reference, s.Material.MaterialNumber, "A-03-01", "OCT|2026",
            [new PulloutPickSource(s.TopLevel.Id, "OCT|2026", 45)], ""), default);
        Assert.Null(s.Db.PulloutPicks.AsNoTracking().Single().PickedAt);
        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(s.Db.PulloutPicks.AsNoTracking().Single().Id, 45), default);
        await ConfirmPicklistAsync(s.Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => change.Handle(new ChangePulloutPickLocationCommand(
            s.Reference, s.Material.MaterialNumber, "A-01-01", "OCT|2026",
            [new PulloutPickSource(s.Other.Id, "OCT|2026", 45)], "Too late"), default));
    }
}

file sealed class PickAllow : IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
