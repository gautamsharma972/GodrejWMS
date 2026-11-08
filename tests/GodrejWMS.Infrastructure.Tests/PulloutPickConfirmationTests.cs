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
        new(db, TestServices.Posting(db), TestServices.Uow(db), new PickAllow(), new SvcClock(), new SvcUser());

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
    public async Task PartialPick_WithAMoveOfTheRemainder_IsOneAtomicChange()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var pick = s.Db.PulloutPicks.Single();

        var result = await Handler(s.Db).Handle(
            new ConfirmPulloutPickCommand(pick.Id, 45, new PulloutRemainderMove(s.Ground.Id, 5)), default);

        Assert.Equal(5, result.MovedBoxes);
        Assert.Equal(0, result.RemainingAtSourceBoxes);
        Assert.Equal("A-02-01", result.DestinationLocationCode);
        Assert.Equal(0, OnHand(s.Db, s.TopLevel));
        Assert.Equal(5, OnHand(s.Db, s.Ground));
        var relocation = s.Db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.PickRelocate).OrderBy(e => e.Id).ToList();
        Assert.Equal(new decimal[] { -5, 5 }, relocation.Select(e => e.QuantityBoxes).ToArray());
        Assert.NotNull(s.Db.PulloutPicks.AsNoTracking().Single().PickedAt);
    }

    [Fact]
    public async Task RemainderCanMoveIntoAVirtualLocation_WhichThenBecomesInUse()
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

        Assert.Equal(5, OnHand(s.Db, spare));
        Assert.Equal(VirtualLocationStatus.InUse, spare.VirtualStatus);
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
    public async Task ShortPick_ReturnsTheMissingBoxesToStock_AndMarksTheLinePartial()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        var result = await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(s.Db.PulloutPicks.Single().Id, 40), default);

        Assert.Equal(5, result.ReturnedToStockBoxes);
        Assert.Equal(10, result.RemainingAtSourceBoxes); // 5 never taken + 5 handed back
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

        // Once the picker has confirmed it, it can no longer be changed.
        await Handler(s.Db).Handle(new ConfirmPulloutPickCommand(pick.Id, 45), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => change.Handle(new ChangePulloutPickLocationCommand(
            s.Reference, s.Material.MaterialNumber, "A-03-01", "OCT|2026",
            [new PulloutPickSource(s.TopLevel.Id, "OCT|2026", 45)], "Too late"), default));
    }
}

file sealed class PickAllow : IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
