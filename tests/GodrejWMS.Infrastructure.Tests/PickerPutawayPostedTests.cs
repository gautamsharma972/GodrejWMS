using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>The picker's put-away screen on an order that was posted to inventory on upload: stock is already at the suggested
/// location, so a change is a movement and GIT / NO RACK a reversal.</summary>
public class PickerPutawayPostedTests
{
    private static async Task<(AppDbContext Db, Material Material, List<PalletPosition> Positions, InwardPutaway Putaway)> SeedAsync()
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

        var submit = new SubmitInwardHandler(
            db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser());
        await submit.Handle(new SubmitInwardCommand([new SubmitInwardLine(material.MaterialNumber, 30, "OCT|2026")], WarehouseId: 1), default);
        return (db, material, rack.PalletPositions.OrderBy(p => p.Column).ToList(), db.InwardPutaways.Single());
    }

    private static PickerPlaceInwardPutawayHandler Handler(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new PPAllow(), new SvcUser(), new SvcClock());

    private static decimal OnHand(AppDbContext db, int positionId) =>
        db.StockBatches.AsNoTracking().Where(b => b.PalletPositionId == positionId).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task PostedGrn_NeedsThePicker_UntilTheyConfirmTheLocation()
    {
        var (db, _, _, putaway) = await SeedAsync();
        using var _ = db;
        var query = new GetPickerPendingInwardHandler(db);

        var pending = await query.Handle(new GetPickerPendingInwardQuery(), default);
        Assert.Single(pending.Items);

        var positionId = putaway.PalletPositionId;
        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", positionId, null), default);

        Assert.Empty((await query.Handle(new GetPickerPendingInwardQuery(), default)).Items);
        Assert.Equal(30, OnHand(db, positionId)); // confirming changes no stock
        Assert.True(db.InwardPutaways.AsNoTracking().Single().IsPickerPlaced);
    }

    [Fact]
    public async Task ChangingTheLocation_MovesThePostedStock()
    {
        var (db, _, positions, putaway) = await SeedAsync();
        using var _ = db;
        var from = putaway.PalletPositionId;
        var to = positions.First(p => p.Id != from).Id;

        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", to, null), default);

        Assert.Equal(0, OnHand(db, from));
        Assert.Equal(30, OnHand(db, to));
        var moved = db.InwardPutaways.AsNoTracking().Single();
        Assert.Equal(to, moved.PalletPositionId);
        Assert.True(moved.IsConfirmed);
        var entries = db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.PutawayChange).OrderBy(e => e.Id).ToList();
        Assert.Equal(new decimal[] { -30, 30 }, entries.Select(e => e.QuantityBoxes).ToArray());
    }

    [Fact]
    public async Task GitTakesTheStockOutOfInventory_AndCorrectingItPutsItBack()
    {
        var (db, _, positions, putaway) = await SeedAsync();
        using var _ = db;
        var original = putaway.PalletPositionId;

        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, null, null, "GIT"), default);
        Assert.Equal(0, OnHand(db, original));
        Assert.Equal("GIT", db.InwardPutaways.AsNoTracking().Single().ExcludedLocation);

        var corrected = positions.First(p => p.Id != original).Id;
        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", corrected, null), default);

        Assert.Equal(30, OnHand(db, corrected));
        var row = db.InwardPutaways.AsNoTracking().Single();
        Assert.Null(row.ExcludedLocation);
        Assert.True(row.IsConfirmed);
    }

    [Fact]
    public async Task ChangingTheLocationOfTheOnlyPalletToAVirtualOne_IsAllowedLikeAnyOther()
    {
        var (db, _, _, putaway) = await SeedAsync();
        using var _ = db;
        var virtualLocation = await TestServices.Virtuals(db).AcquireAsync(1, null);

        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", virtualLocation.Id, null, AcknowledgeWarning: true), default);

        Assert.Equal(30, OnHand(db, virtualLocation.Id));
    }
}

file sealed class PPAllow : IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
