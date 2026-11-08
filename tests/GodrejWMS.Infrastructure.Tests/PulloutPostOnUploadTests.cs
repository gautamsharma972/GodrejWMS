using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutPostOnUploadTests
{
    private static async Task<(AppDbContext Db, Material Material, PalletPosition Physical, PalletPosition Virtual)> SeedAsync(
        decimal physicalQuantity, int physicalPkm, decimal virtualQuantity, int virtualPkm)
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 });
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var virtualLocation = await TestServices.Virtuals(db).AcquireAsync(1, null);
        var physical = rack.PalletPositions.Single();
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = material.Id, PalletPositionId = physical.Id, MfgMonth = physicalPkm, QuantityBoxes = physicalQuantity },
            new StockBatch { MaterialId = material.Id, PalletPositionId = virtualLocation.Id, MfgMonth = virtualPkm, QuantityBoxes = virtualQuantity });
        await db.SaveChangesAsync();
        return (db, material, physical, virtualLocation);
    }

    private static SubmitPulloutHandler Handler(AppDbContext db, bool virtualFirst = true) =>
        new(db, new PulloutAllocationService(db, Options.Create(new GodrejWMS.Application.Common.PickingOptions { PickFromVirtualFirst = virtualFirst })),
            TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());

    private static Task<GodrejWMS.Application.Features.Pullout.Dtos.PulloutResultDto> UploadAsync(AppDbContext db, int quantity, bool virtualFirst = true) =>
        Handler(db, virtualFirst).Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, quantity)], WarehouseId: 1), default);

    [Fact]
    public async Task Upload_DeductsTheStockImmediately_AndWritesTheLedger()
    {
        var (db, _, physical, _) = await SeedAsync(50, 202610, 30, 202611);
        using var _ = db;

        var result = await UploadAsync(db, 20);

        Assert.Equal(20, Assert.Single(result.Lines).PickedQuantityBoxes);
        var pullout = db.PulloutTransactions.Single();
        Assert.True(pullout.IsConfirmed);
        Assert.Equal(30, db.StockBatches.Single(b => b.PalletPositionId == physical.Id).QuantityBoxes);
        var entry = Assert.Single(db.StockLedgerEntries.Where(e => e.EntryType == StockLedgerEntryTypes.PulloutDeduct));
        Assert.Equal(-20, entry.QuantityBoxes);
        Assert.Equal(pullout.ReferenceNumber, entry.ReferenceNumber);
        Assert.All(db.PulloutPicks, p => Assert.Null(p.PickedAt));
    }

    [Fact]
    public async Task PickingTheLastBoxesOfAVirtualLocation_ReleasesItForReuse()
    {
        var (db, _, _, virtualLocation) = await SeedAsync(50, 202611, 30, 202610); // the virtual one holds the older PKM
        using var _ = db;

        await UploadAsync(db, 30);

        Assert.Equal(0, db.StockBatches.Single(b => b.PalletPositionId == virtualLocation.Id).QuantityBoxes);
        Assert.Equal(VirtualLocationStatus.Free, virtualLocation.VirtualStatus);
        Assert.NotNull(virtualLocation.LastReleasedAt);
        Assert.All(db.VirtualLocationLogs, l => Assert.NotNull(l.ReleasedAt));
        // ...and the next overflow pallet reuses that very location.
        var reused = await TestServices.Virtuals(db).AcquireAsync(1, null);
        Assert.Equal(virtualLocation.Id, reused.Id);
    }

    [Fact]
    public async Task PickFromVirtualFirst_AppliesOnlyWithinTheSamePkm()
    {
        // Same PKM in both: the virtual location is picked before the physical one.
        var (db, _, physical, virtualLocation) = await SeedAsync(50, 202610, 50, 202610);
        using (db)
        {
            var result = await UploadAsync(db, 20);
            Assert.Equal(virtualLocation.LocationCode, Assert.Single(Assert.Single(result.Lines).Picks).LocationCode);
        }

        // With the option off the usual rack order wins.
        (db, _, physical, virtualLocation) = await SeedAsync(50, 202610, 50, 202610);
        using (db)
        {
            var result = await UploadAsync(db, 20, virtualFirst: false);
            Assert.Equal(physical.LocationCode, Assert.Single(Assert.Single(result.Lines).Picks).LocationCode);
        }

        // An older PKM in a physical location still goes before a newer PKM in a virtual one: FIFO is never broken.
        (db, _, physical, virtualLocation) = await SeedAsync(50, 202609, 50, 202611);
        using (db)
        {
            var result = await UploadAsync(db, 20);
            Assert.Equal(physical.LocationCode, Assert.Single(Assert.Single(result.Lines).Picks).LocationCode);
        }
    }

    [Fact]
    public async Task ReUploadingTheSamePullout_IsRefused_AndDeductsNothingTwice()
    {
        var (db, _, physical, _) = await SeedAsync(50, 202610, 30, 202611);
        using var _ = db;
        var first = await UploadAsync(db, 20);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync(db, 20));

        Assert.Contains(first.ReferenceNumber, ex.Message);
        Assert.Equal(30, db.StockBatches.Single(b => b.PalletPositionId == physical.Id).QuantityBoxes);
        Assert.Single(db.PulloutTransactions);
    }
}
