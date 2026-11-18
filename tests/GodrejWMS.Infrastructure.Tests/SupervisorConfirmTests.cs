using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>The picker only submits; the final confirm of a put-away and of a pick-list belongs to a supervisor, who can also do the
/// picker's submit when no picker is available.</summary>
public class SupervisorConfirmTests
{
    private sealed class Perms(params string[] keys) : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(keys.Contains(permissionKey));
    }

    private static readonly Perms Picker = new(PermissionKeys.Inward.AssignPkm, PermissionKeys.Pullout.Pick);
    private static readonly Perms Supervisor = new(
        PermissionKeys.Inward.AssignPkm, PermissionKeys.Pullout.Pick, PermissionKeys.Inward.Confirm, PermissionKeys.Pullout.Confirm);

    private static async Task<(AppDbContext Db, List<PalletPosition> Positions)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 3; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        db.Materials.Add(new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        });
        await db.SaveChangesAsync();
        await new SubmitInwardHandler(
                db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(40000001, 30, "OCT|2026")], WarehouseId: 1), default);
        return (db, rack.PalletPositions.OrderBy(p => p.Column).ToList());
    }

    private static PickerPlaceInwardPutawayHandler Place(AppDbContext db, IPermissionService permissions) =>
        new(db, TestServices.Posting(db), TestServices.Git(db), TestServices.Uow(db), permissions, new SvcUser(), new SvcClock());

    private static ConfirmPickerPutawayHandler ConfirmPutaway(AppDbContext db, IPermissionService permissions) =>
        new(db, permissions, TestServices.Posting(db), new SvcClock(), new SvcUser());

    [Fact]
    public async Task PickerSubmits_ButOnlyASupervisorCanConfirmThePutaway()
    {
        var (db, positions) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        var target = positions.First(p => p.Id != putaway.PalletPositionId).Id;

        // The picker submits the location.
        await Place(db, Picker).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", target, null, AcknowledgeWarning: true), default);

        // ... but cannot confirm.
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfirmPutaway(db, Picker).Handle(new ConfirmPickerPutawayCommand(transactionId), default));
        Assert.Contains("Only a supervisor", denied.Message);
        Assert.False(db.InwardPutaways.AsNoTracking().Single().IsExcludedLocked);

        // The supervisor confirms.
        await ConfirmPutaway(db, Supervisor).Handle(new ConfirmPickerPutawayCommand(transactionId), default);
        Assert.True(db.InwardPutaways.AsNoTracking().Single().IsExcludedLocked);
    }

    [Fact]
    public async Task ASubmittedGrn_LeavesThePickersList_ButStaysOnTheSupervisorsUntilConfirmed()
    {
        var (db, positions) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        var target = positions.First(p => p.Id != putaway.PalletPositionId).Id;

        Assert.Single((await new GetPickerPendingInwardHandler(db, Picker).Handle(new GetPickerPendingInwardQuery(), default)).Items);
        Assert.Equal(1, (await new GetPickerPendingCountsHandler(db, Picker).Handle(new GetPickerPendingCountsQuery(), default)).PutawayGrns);

        await Place(db, Picker).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", target, null, AcknowledgeWarning: true), default);

        Assert.Empty((await new GetPickerPendingInwardHandler(db, Picker).Handle(new GetPickerPendingInwardQuery(), default)).Items);
        Assert.Equal(0, (await new GetPickerPendingCountsHandler(db, Picker).Handle(new GetPickerPendingCountsQuery(), default)).PutawayGrns);
        Assert.Single((await new GetPickerPendingInwardHandler(db, Supervisor).Handle(new GetPickerPendingInwardQuery(), default)).Items);
        Assert.Equal(1, (await new GetPickerPendingCountsHandler(db, Supervisor).Handle(new GetPickerPendingCountsQuery(), default)).PutawayGrns);
    }

    [Fact]
    public async Task ASupervisor_CanSubmitInThePickersAbsence_AndTheQuantityCanNeverExceedTheInboundVolume()
    {
        var (db, positions) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        var target = positions.First(p => p.Id != putaway.PalletPositionId).Id;

        var tooMany = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Place(db, Supervisor).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 31, "OCT|2026", target, null, AcknowledgeWarning: true), default));
        Assert.Contains("more than the 30", tooMany.Message);

        // A smaller quantity (the rest is not put away) is a normal supervisor correction.
        var saved = await Place(db, Supervisor).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 25, "OCT|2026", target, null, AcknowledgeWarning: true), default);
        Assert.True(saved.Saved);
        var row = db.InwardPutaways.AsNoTracking().Single();
        Assert.True(row.IsPickerPlaced);
        Assert.Equal(25, row.QuantityBoxes);
    }

    [Fact]
    public async Task PickListConfirm_NeedsASupervisor_AndASubmittedPickListLeavesThePickersList()
    {
        var (db, _) = await SeedAsync();
        using var _ = db;
        await new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, 10)], WarehouseId: 1), default);
        var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;
        var pick = db.PulloutPicks.AsNoTracking().Single();

        Assert.Single((await new GetPickerPendingPulloutsHandler(db, Picker).Handle(new GetPickerPendingPulloutsQuery(), default)).Items);

        // The picker submits the pick (a supervisor could do the same).
        await new ConfirmPulloutPickHandler(db, Picker, new SvcClock(), new SvcUser()).Handle(new ConfirmPulloutPickCommand(pick.Id, 10), default);

        Assert.Empty((await new GetPickerPendingPulloutsHandler(db, Picker).Handle(new GetPickerPendingPulloutsQuery(), default)).Items);
        Assert.Single((await new GetPickerPendingPulloutsHandler(db, Supervisor).Handle(new GetPickerPendingPulloutsQuery(), default)).Items);

        ConfirmPulloutPicklistHandler Confirm(IPermissionService p) =>
            new(db, TestServices.Posting(db), TestServices.Uow(db), p, new SvcClock(), new SvcUser());
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => Confirm(Picker).Handle(new ConfirmPulloutPicklistCommand(pulloutId), default));
        Assert.Contains("Only a supervisor", denied.Message);

        await Confirm(Supervisor).Handle(new ConfirmPulloutPicklistCommand(pulloutId), default);
        Assert.NotNull(db.PulloutTransactions.AsNoTracking().Single().ConfirmedAt);
    }

    [Fact]
    public async Task APullout_WithNoPicks_AndAPutawayWithNoBoxes_NeverAppearOnAnyQueueOrBadge()
    {
        var (db, _) = await SeedAsync();
        using var _ = db;

        // A pullout for a material with no stock allocates nothing: "0 of 0 picked".
        db.Materials.Add(new Material
        {
            MaterialNumber = 40000009, Description = "No stock", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        });
        await db.SaveChangesAsync();
        await new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000009, 5)], WarehouseId: 1), default);
        Assert.Empty(db.PulloutPicks.AsNoTracking());

        foreach (var who in new IPermissionService[] { Picker, Supervisor })
        {
            Assert.Empty((await new GetPickerPendingPulloutsHandler(db, who).Handle(new GetPickerPendingPulloutsQuery(), default)).Items);
            Assert.Equal(0, (await new GetPickerPendingCountsHandler(db, who).Handle(new GetPickerPendingCountsQuery(), default)).PulloutPicklists);
        }

        // A GRN whose only put-away has no boxes (all of them recorded away) is not a task either.
        var row = await db.InwardPutaways.SingleAsync();
        row.QuantityBoxes = 0;
        await db.SaveChangesAsync();
        foreach (var who in new IPermissionService[] { Picker, Supervisor })
        {
            Assert.Empty((await new GetPickerPendingInwardHandler(db, who).Handle(new GetPickerPendingInwardQuery(), default)).Items);
            Assert.Equal(0, (await new GetPickerPendingCountsHandler(db, who).Handle(new GetPickerPendingCountsQuery(), default)).PutawayGrns);
        }
    }
}
