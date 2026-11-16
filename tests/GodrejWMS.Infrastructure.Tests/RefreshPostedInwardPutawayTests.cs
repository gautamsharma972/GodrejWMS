using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RefreshPostedInwardPutawayTests
{
    private static async Task<(AppDbContext Db, Material Material, int TransactionId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 6, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 6; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 65, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var submit = new SubmitInwardHandler(
            db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser());
        await submit.Handle(new SubmitInwardCommand([new SubmitInwardLine(material.MaterialNumber, 130, "OCT|2026")], WarehouseId: 1), default);
        return (db, material, db.InwardTransactions.AsNoTracking().Single().Id);
    }

    private static RefreshPostedInwardPutawayHandler Handler(AppDbContext db) =>
        new(db, new PalletAllocationService(db, new SvcClock()), TestServices.Posting(db), TestServices.Virtuals(db),
            TestServices.Uow(db), new SvcClock(), new SvcUser());

    [Fact]
    public async Task Refresh_ReallocatesUntouchedPutaways_ToTheNewPalletSize_AndLeavesPickerPlacedOnesAlone()
    {
        var (db, material, transactionId) = await SeedAsync();
        using var _ = db;
        // 130 boxes at pallet size 65 = two pallets. The picker has submitted the first; the second is untouched.
        var putaways = db.InwardPutaways.Include(p => p.PalletPosition).OrderBy(p => p.Id).ToList();
        Assert.Equal(2, putaways.Count);
        putaways[0].IsPickerPlaced = true;
        var keptLocation = putaways[0].PalletPosition.LocationCode;
        material.PalletCapacityBoxes = 40; // pallet size reduced
        await db.SaveChangesAsync();

        await Handler(db).Handle(new RefreshPostedInwardPutawayCommand(transactionId), default);

        var after = db.InwardPutaways.AsNoTracking().Include(p => p.PalletPosition).ToList();
        Assert.Contains(after, p => p.IsPickerPlaced && p.PalletPosition.LocationCode == keptLocation && p.QuantityBoxes == 65);
        // The untouched 65 boxes are laid out again with pallets of 40: 40 + 25.
        var redone = after.Where(p => !p.IsPickerPlaced).OrderByDescending(p => p.QuantityBoxes).ToList();
        Assert.Equal([40m, 25m], redone.Select(p => p.QuantityBoxes).ToList());
        Assert.All(redone, p => Assert.True(p.IsConfirmed));
        // Inventory still holds exactly the 130 received boxes.
        Assert.Equal(130m, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
    }

    [Fact]
    public async Task Refresh_AllocatesTheBoxesAnInactiveMaterialWasShortOf_OnceItIsActiveAgain()
    {
        var (db, material, transactionId) = await SeedAsync();
        using var _ = db;
        // Simulate an upload while the material was inactive: nothing allocated.
        var line = db.InwardTransactionLines.Single();
        db.InwardPutaways.RemoveRange(db.InwardPutaways);
        db.StockBatches.RemoveRange(db.StockBatches);
        line.AllocatedQuantityBoxes = 0;
        line.Status = GodrejWMS.Domain.Enums.AllocationStatus.Failed;
        await db.SaveChangesAsync();

        await Handler(db).Handle(new RefreshPostedInwardPutawayCommand(transactionId), default);

        Assert.Equal(130m, db.InwardPutaways.AsNoTracking().Sum(p => p.QuantityBoxes));
        Assert.Equal(130m, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Fulfilled, db.InwardTransactionLines.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Refresh_TurnsAReservationThatNeverBecameStock_IntoPostedStock()
    {
        var (db, _, transactionId) = await SeedAsync();
        using var _ = db;
        // A reservation left by the old Reallocate on a posted GRN: a put-away row with no stock behind it.
        var line = db.InwardTransactionLines.Single();
        var free = db.PalletPositions.AsNoTracking().OrderBy(p => p.Id).Last();
        db.InwardPutaways.Add(new GodrejWMS.Domain.Entities.InwardPutaway
        {
            InwardTransactionLineId = line.Id, PalletPositionId = free.Id, QuantityBoxes = 20, IsConfirmed = false
        });
        line.RequestedQuantityBoxes += 20; // line.Allocated deliberately stays at 130: the reservation is not counted in it
        await db.SaveChangesAsync();
        var stockBefore = db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes);

        await Handler(db).Handle(new RefreshPostedInwardPutawayCommand(transactionId), default);

        Assert.All(db.InwardPutaways.AsNoTracking(), p => Assert.True(p.IsConfirmed));
        Assert.Equal(stockBefore + 20, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
        Assert.Equal(150m, db.InwardPutaways.AsNoTracking().Sum(p => p.QuantityBoxes));
    }

    [Fact]
    public async Task Refresh_NeverPostsMoreThanTheGrnAskedFor_AndCorrectsAnEarlierOverCount()
    {
        var (db, _, transactionId) = await SeedAsync();
        using var _ = db;
        // The bad state an earlier refresh left: 20 extra boxes posted, so put-aways add up to 150 against 130 requested.
        var line = db.InwardTransactionLines.Single();
        var extra = db.PalletPositions.AsNoTracking().OrderBy(p => p.Id).Last();
        db.InwardPutaways.Add(new GodrejWMS.Domain.Entities.InwardPutaway
        {
            InwardTransactionLineId = line.Id, PalletPositionId = extra.Id, QuantityBoxes = 20, IsConfirmed = true
        });
        db.StockBatches.Add(new GodrejWMS.Domain.Entities.StockBatch
        {
            MaterialId = line.MaterialId, PalletPositionId = extra.Id, MfgMonth = line.MfgMonth!.Value, QuantityBoxes = 20
        });
        await db.SaveChangesAsync();
        Assert.Equal(150m, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));

        await Handler(db).Handle(new RefreshPostedInwardPutawayCommand(transactionId), default);

        Assert.Equal(130m, db.InwardPutaways.AsNoTracking().Sum(p => p.QuantityBoxes));
        Assert.Equal(130m, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
    }

    [Fact]
    public async Task Refresh_NeverPutsTwoMaterialsInOneLocation()
    {
        var db = TestDbContextFactory.Create();
        using var _ = db;
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 8, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 8; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        foreach (var number in new long[] { 40000001, 40000002 })
        {
            db.Materials.Add(new Material
            {
                MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
                LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
            });
        }

        await db.SaveChangesAsync();
        var submit = new SubmitInwardHandler(
            db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser());
        await submit.Handle(new SubmitInwardCommand(
            [new SubmitInwardLine(40000001, 80, "OCT|2026"), new SubmitInwardLine(40000002, 80, "OCT|2026")], WarehouseId: 1), default);
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;

        await Handler(db).Handle(new RefreshPostedInwardPutawayCommand(transactionId), default);

        var perLocation = db.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes > 0)
            .GroupBy(b => b.PalletPositionId).Select(g => g.Select(b => b.MaterialId).Distinct().Count()).ToList();
        Assert.All(perLocation, count => Assert.Equal(1, count));
        Assert.Equal(160m, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
    }

    [Fact]
    public async Task Refresh_Throws_WhenThePickerHasActionedEveryPutaway()
    {
        var (db, _, transactionId) = await SeedAsync();
        using var _ = db;
        foreach (var p in db.InwardPutaways) p.IsPickerPlaced = true;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db).Handle(new RefreshPostedInwardPutawayCommand(transactionId), default));
    }
}
