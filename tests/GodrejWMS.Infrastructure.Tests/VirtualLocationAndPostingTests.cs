using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class VirtualLocationAndPostingTests
{
    private static async Task<(AppDbContext Db, Material Material, PalletPosition Physical)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 2, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 });
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        return (db, material, rack.PalletPositions.Single());
    }

    [Fact]
    public async Task Acquire_CreatesCodedVirtualLocationsInTheSystemRack_AndMarksThemInUse()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var virtuals = TestServices.Virtuals(db);

        var first = await virtuals.AcquireAsync(1, 7);
        var second = await virtuals.AcquireAsync(1, 7);

        Assert.Equal("VL-GDR1-000001", first.LocationCode);
        Assert.Equal("VL-GDR1-000002", second.LocationCode);
        Assert.All(new[] { first, second }, p =>
        {
            Assert.True(p.IsVirtual);
            Assert.Equal(VirtualLocationStatus.InUse, p.VirtualStatus);
            Assert.Equal(1, p.MaxPallets);
            Assert.Equal(ZoneTypeIds.Overflow, p.ZoneTypeId);
        });
        var rack = await db.Racks.SingleAsync(r => r.IsSystem);
        Assert.Equal(rack.Id, first.RackId);
        Assert.Equal(2, db.VirtualLocationLogs.Count(l => l.InwardTransactionId == 7 && l.ReleasedAt == null));
    }

    [Fact]
    public async Task Acquire_ReusesTheFreeLocationWithTheLowestSequence()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var virtuals = TestServices.Virtuals(db);
        var one = await virtuals.AcquireAsync(1, null);
        var two = await virtuals.AcquireAsync(1, null);
        var three = await virtuals.AcquireAsync(1, null);
        foreach (var released in new[] { three, one })
        {
            Assert.True(await virtuals.ReleaseIfEmptyAsync(released));
        }

        await db.SaveChangesAsync();
        Assert.Equal(VirtualLocationStatus.Free, one.VirtualStatus);
        Assert.NotNull(one.LastReleasedAt);

        var reused = await virtuals.AcquireAsync(1, 9);
        var reusedNext = await virtuals.AcquireAsync(1, 9);
        var created = await virtuals.AcquireAsync(1, 9);

        Assert.Equal(one.Id, reused.Id);          // lowest sequence first
        Assert.Equal(three.Id, reusedNext.Id);
        Assert.Equal("VL-GDR1-000004", created.LocationCode); // none free: a new one
        Assert.Equal(VirtualLocationStatus.InUse, two.VirtualStatus);
    }

    [Fact]
    public async Task Posting_UpdatesStockWritesLedger_AndReleasesVirtualLocationOnlyWhenEmpty()
    {
        var (db, material, physical) = await SeedAsync();
        using var _ = db;
        var virtuals = TestServices.Virtuals(db);
        var posting = TestServices.Posting(db);
        var virtualLocation = await virtuals.AcquireAsync(1, null);

        await posting.ApplyAsync(new StockPostingContext(StockLedgerEntryTypes.InwardPost, "GRN", "GRN-1"),
            [new StockChange(material.Id, 202610, LocationSubtypeIds.Good, virtualLocation, 50)]);
        await db.SaveChangesAsync();
        Assert.Equal(VirtualLocationStatus.InUse, virtualLocation.VirtualStatus);

        // Move 20 to a physical bin: 30 stay, so the virtual location is not released.
        await posting.ApplyAsync(new StockPostingContext(StockLedgerEntryTypes.InventoryMovement, "Move", "M-1"),
            [new StockChange(material.Id, 202610, LocationSubtypeIds.Good, virtualLocation, -20),
             new StockChange(material.Id, 202610, LocationSubtypeIds.Good, physical, 20)]);
        await db.SaveChangesAsync();
        Assert.Equal(VirtualLocationStatus.InUse, virtualLocation.VirtualStatus);

        // Take the remaining 30: now it is empty, so it is released in the same unit of work.
        await posting.ApplyAsync(new StockPostingContext(StockLedgerEntryTypes.PulloutDeduct, "PICK", "PICK-1"),
            [new StockChange(material.Id, 202610, LocationSubtypeIds.Good, virtualLocation, -30)]);
        await db.SaveChangesAsync();

        Assert.Equal(VirtualLocationStatus.Free, virtualLocation.VirtualStatus);
        Assert.All(db.VirtualLocationLogs, l => Assert.NotNull(l.ReleasedAt));
        var batches = db.StockBatches.ToList();
        Assert.Equal(0, batches.Single(b => b.PalletPositionId == virtualLocation.Id).QuantityBoxes); // row kept at zero
        Assert.Equal(20, batches.Single(b => b.PalletPositionId == physical.Id).QuantityBoxes);
        var ledger = db.StockLedgerEntries.OrderBy(e => e.Id).ToList();
        Assert.Equal(new decimal[] { 50, -20, 20, -30 }, ledger.Select(e => e.QuantityBoxes).ToArray());
        Assert.Equal(30, ledger[1].BalanceAfterBoxes);
        Assert.Equal(1, ledger.Select(e => e.WarehouseId).Distinct().Single());
        Assert.Equal(2, ledger.Where(e => e.PostingNumber == ledger[1].PostingNumber).Count()); // a move is two entries, one posting
    }

    [Fact]
    public async Task Posting_RefusesToTakeMoreThanIsOnHand_AndNothingIsChanged()
    {
        var (db, material, physical) = await SeedAsync();
        using var _ = db;
        var posting = TestServices.Posting(db);
        await posting.ApplyAsync(new StockPostingContext(StockLedgerEntryTypes.InwardPost),
            [new StockChange(material.Id, 202610, LocationSubtypeIds.Good, physical, 10)]);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => posting.ApplyAsync(
            new StockPostingContext(StockLedgerEntryTypes.PulloutDeduct),
            [new StockChange(material.Id, 202610, LocationSubtypeIds.Good, physical, -11)]));

        Assert.Contains("holds only 10", ex.Message);
        Assert.Equal(10, db.StockBatches.Single().QuantityBoxes);
        Assert.Single(db.StockLedgerEntries);
    }
}
