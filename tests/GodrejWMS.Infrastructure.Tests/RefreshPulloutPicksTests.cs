using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RefreshPulloutPicksTests
{
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
        return (db, rack.PalletPositions.OrderBy(p => p.Column).ToList());
    }

    private static async Task UploadInwardAsync(AppDbContext db, decimal quantity, string pkm)
    {
        var submit = new SubmitInwardHandler(
            db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser());
        await submit.Handle(new SubmitInwardCommand([new SubmitInwardLine(40000001, quantity, pkm)], WarehouseId: 1), default);
    }

    private static Task PullOutAsync(AppDbContext db, decimal quantity) =>
        new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, quantity)], WarehouseId: 1), default);

    private static decimal Total(AppDbContext db) => db.StockBatches.AsNoTracking().Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task RejectedGrn_ThenRefreshPick_ReallocatesFromOtherStock_AndDropsTheInvalidPick()
    {
        var (db, _) = await SeedAsync();
        using var _ = db;
        await UploadInwardAsync(db, 30, "SEP|2026");   // older stock, GRN 1
        await UploadInwardAsync(db, 30, "OCT|2026");   // GRN 2 (unplaced)
        await PullOutAsync(db, 50);                    // 30 from SEP (placed-state irrelevant: both unplaced) + 20 from OCT
        var pullout = db.PulloutTransactions.AsNoTracking().Single();

        // Reject the GRN that holds the SEP boxes the pullout reserved.
        var grn = db.InwardTransactions.AsNoTracking().OrderBy(t => t.Id).First();
        await new RejectInwardHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new RejectInwardCommand(grn.Id, "Wrong GRN"), default);
        var detail = await new GetPulloutDetailHandler(db).Handle(new GetPulloutDetailQuery(pullout.Id), default);
        Assert.True(detail.CanRefreshPick);

        var result = await new RefreshPulloutPicksHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock())
            .Handle(new RefreshPulloutPicksCommand(pullout.ReferenceNumber), default);

        Assert.Equal(30, result.DroppedBoxes);
        // Only the other GRN's remaining 10 boxes are left to allocate: the line is still 20 short.
        Assert.Equal(10, result.AllocatedBoxes);
        Assert.Equal(20, result.StillShortBoxes);
        Assert.Empty(db.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes < 0));
    }

    [Fact]
    public async Task RefreshPick_IsRefused_OnceThePickListIsConfirmed()
    {
        var (db, _) = await SeedAsync();
        using var _ = db;
        await UploadInwardAsync(db, 30, "OCT|2026");
        await PullOutAsync(db, 10);
        var pullout = await db.PulloutTransactions.SingleAsync();
        pullout.ConfirmedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RefreshPulloutPicksHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock())
                .Handle(new RefreshPulloutPicksCommand(pullout.ReferenceNumber), default));
    }

    [Fact]
    public async Task PickingZero_SkipsTheMaterial_ReturnsEveryBoxToStock_AndOpensTheReservedPutawayAgain()
    {
        var (db, _) = await SeedAsync();
        using var _ = db;
        await UploadInwardAsync(db, 30, "OCT|2026"); // posted, still on the vehicle (picker has not placed it)
        await PullOutAsync(db, 30);                  // the pullout took all of it: the put-away becomes PICK-RESERVED
        Assert.Equal("PICK-RESERVED", db.InwardPutaways.AsNoTracking().Single().ExcludedLocation);
        Assert.Equal(0, Total(db));

        // Zero is a valid confirmation: the picker skips the material.
        var pick = db.PulloutPicks.AsNoTracking().Single();
        var result = await new ConfirmPulloutPickHandler(db, new AllowPicks(), new SvcClock(), new SvcUser())
            .Handle(new ConfirmPulloutPickCommand(pick.Id, 0), default);
        Assert.Equal(30, result.ReturnedToStockBoxes);
        Assert.True(new ConfirmPulloutPickValidator().Validate(new ConfirmPulloutPickCommand(pick.Id, 0)).IsValid);
        Assert.False(new ConfirmPulloutPickValidator().Validate(new ConfirmPulloutPickCommand(pick.Id, -1)).IsValid);

        var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;
        await new ConfirmPulloutPicklistHandler(db, TestServices.Posting(db), TestServices.Uow(db), new AllowPicks(), new SvcClock(), new SvcUser())
            .Handle(new ConfirmPulloutPicklistCommand(pulloutId), default);

        // Every allocated box is back on the shelf and the put-away is an ordinary, unplaced one again.
        Assert.Equal(30, Total(db));
        var line = db.PulloutTransactionLines.AsNoTracking().Single();
        Assert.Equal(0, line.PickedQuantityBoxes);
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Failed, line.Status);
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        Assert.Null(putaway.ExcludedLocation);
        Assert.False(putaway.IsPickerPlaced);
        Assert.False(putaway.IsExcludedLocked);
        Assert.Equal(30, putaway.QuantityBoxes);
    }
}

file sealed class AllowPicks : GodrejWMS.Application.Common.Permissions.IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
