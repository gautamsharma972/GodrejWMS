using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class InwardPostOnUploadTests
{
    /// <summary>Warehouse 1 with a rack of <paramref name="physicalPositions"/> one-pallet bins and one material with a 50-box pallet.</summary>
    private static async Task<(AppDbContext Db, Material Material, List<PalletPosition> Positions)> SeedAsync(int physicalPositions)
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = physicalPositions, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= physicalPositions; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        db.Materials.Add(NewMaterial(40000001));
        db.Materials.Add(NewMaterial(40000002));
        await db.SaveChangesAsync();
        return (db, db.Materials.Single(m => m.MaterialNumber == 40000001), rack.PalletPositions.OrderBy(p => p.Column).ToList());
    }

    private static Material NewMaterial(long number) => new()
    {
        MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
        LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
    };

    private static SubmitInwardHandler Handler(AppDbContext db) =>
        new(db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser());

    private static Task<GodrejWMS.Application.Features.Inward.Dtos.InwardResultDto> SubmitAsync(
        AppDbContext db, long material, int quantity, string pkm = "OCT|2026", string? vehicle = null, bool fromFile = false) =>
        Handler(db).Handle(new SubmitInwardCommand([new SubmitInwardLine(material, quantity, pkm)], WarehouseId: 1, VehicleNumber: vehicle, CheckDuplicateUpload: fromFile), default);

    /// <summary>Fills every given physical bin with stock of the other material so the allocator cannot use it.</summary>
    private static async Task FillWithOtherMaterialAsync(AppDbContext db, IEnumerable<PalletPosition> positions)
    {
        var other = db.Materials.Single(m => m.MaterialNumber == 40000002);
        foreach (var position in positions)
        {
            db.StockBatches.Add(new StockBatch { MaterialId = other.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 50 });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FullyPlacedInPhysicalLocations_IsPostedToInventoryOnUpload()
    {
        var (db, material, _) = await SeedAsync(physicalPositions: 3);
        using var _ = db;

        var result = await SubmitAsync(db, material.MaterialNumber, 120);

        var line = Assert.Single(result.Lines);
        Assert.Equal(120, line.AllocatedQuantityBoxes);
        Assert.True(line.IsConfirmed);
        Assert.All(line.AllocatedTo, a => { Assert.True(a.IsConfirmed); Assert.False(a.IsVirtual); });
        Assert.Equal(120, db.StockBatches.Sum(b => b.QuantityBoxes)); // visible in Inventory Master straight away
        Assert.All(db.InwardPutaways, p => Assert.True(p.IsConfirmed));
        Assert.Equal(120, db.StockLedgerEntries.Where(e => e.EntryType == StockLedgerEntryTypes.InwardPost).Sum(e => e.QuantityBoxes));
        Assert.NotNull(db.InwardTransactions.Single().PostedAt);
        Assert.Empty(db.PalletPositions.Where(p => p.IsVirtual));
    }

    [Fact]
    public async Task OnePhysicalPositionFree_120Boxes_Goes50Physical_50ReusedVirtual_20NewVirtual()
    {
        var (db, material, _) = await SeedAsync(physicalPositions: 1);
        using var _ = db;
        var virtuals = TestServices.Virtuals(db);
        var spare = await virtuals.AcquireAsync(1, null);
        await virtuals.ReleaseIfEmptyAsync(spare);
        await db.SaveChangesAsync();
        Assert.Equal(VirtualLocationStatus.Free, spare.VirtualStatus);

        var result = await SubmitAsync(db, material.MaterialNumber, 120);

        var placed = Assert.Single(result.Lines).AllocatedTo.OrderBy(a => a.LocationCode).ToList();
        Assert.Equal(new[] { "A-01-01", "VL-GDR1-000001", "VL-GDR1-000002" }, placed.Select(a => a.LocationCode).ToArray());
        Assert.Equal(new decimal[] { 50, 50, 20 }, placed.Select(a => a.QuantityBoxes).ToArray());
        Assert.Equal(new[] { false, true, true }, placed.Select(a => a.IsVirtual).ToArray());
        var virtualLocations = db.PalletPositions.Where(p => p.IsVirtual).OrderBy(p => p.Column).ToList();
        Assert.Equal(spare.Id, virtualLocations[0].Id); // the free one was reused, not a new code
        Assert.All(virtualLocations, v => Assert.Equal(VirtualLocationStatus.InUse, v.VirtualStatus));
        Assert.Equal(120, db.StockBatches.Sum(b => b.QuantityBoxes));
        Assert.Equal(2, db.VirtualLocationLogs.Count(l => l.InwardTransactionId == result.Id && l.ReleasedAt == null));
    }

    [Fact]
    public async Task AllPhysicalLocationsFull_EveryPalletGoesToItsOwnVirtualLocation()
    {
        var (db, material, positions) = await SeedAsync(physicalPositions: 2);
        using var _ = db;
        await FillWithOtherMaterialAsync(db, positions);

        var result = await SubmitAsync(db, material.MaterialNumber, 120);

        var line = Assert.Single(result.Lines);
        Assert.Equal(120, line.AllocatedQuantityBoxes);
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Fulfilled, line.Status);
        Assert.Equal(new decimal[] { 50, 50, 20 }, line.AllocatedTo.OrderBy(a => a.LocationCode).Select(a => a.QuantityBoxes).ToArray());
        Assert.All(line.AllocatedTo, a => Assert.True(a.IsVirtual));
        Assert.Equal(3, line.AllocatedTo.Select(a => a.LocationCode).Distinct().Count()); // N overflow pallets = N virtual locations
        Assert.Equal(3, db.PalletPositions.Count(p => p.IsVirtual));
    }

    [Fact]
    public async Task PartialPalletOnAVirtualLocation_IsToppedUpLikeAPhysicalOne()
    {
        var (db, material, positions) = await SeedAsync(physicalPositions: 1);
        using var _ = db;
        await FillWithOtherMaterialAsync(db, positions);

        await SubmitAsync(db, material.MaterialNumber, 30);
        var second = await SubmitAsync(db, material.MaterialNumber, 15, vehicle: "MH01");

        var virtualLocation = Assert.Single(db.PalletPositions.Where(p => p.IsVirtual));
        var batch = db.StockBatches.Single(b => b.PalletPositionId == virtualLocation.Id);
        Assert.Equal(45, batch.QuantityBoxes); // same SKU + PKM: topped up, no new virtual location
        var allocated = Assert.Single(Assert.Single(second.Lines).AllocatedTo);
        Assert.Equal(virtualLocation.LocationCode, allocated.LocationCode);
    }

    [Fact]
    public async Task ReUploadingTheSameFile_IsRefused_AndPostsNothingTwice()
    {
        var (db, material, _) = await SeedAsync(physicalPositions: 3);
        using var _ = db;
        var first = await SubmitAsync(db, material.MaterialNumber, 60, fromFile: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitAsync(db, material.MaterialNumber, 60, fromFile: true));

        Assert.Contains(first.ReferenceNumber, ex.Message);
        Assert.Equal(60, db.StockBatches.Sum(b => b.QuantityBoxes));
        Assert.Single(db.InwardTransactions);
    }

    [Fact]
    public async Task ManualEntry_OfTheSameMaterialAndQuantity_IsNotTreatedAsAReUpload()
    {
        var (db, material, _) = await SeedAsync(physicalPositions: 3);
        using var _ = db;
        await SubmitAsync(db, material.MaterialNumber, 20);

        await SubmitAsync(db, material.MaterialNumber, 20);

        Assert.Equal(2, db.InwardTransactions.Count());
        Assert.Equal(40, db.StockBatches.Sum(b => b.QuantityBoxes));
    }

    [Fact]
    public async Task WithoutPkm_KeepsTheOpenReservation_AndOverflowsToAReservedVirtualLocation()
    {
        var (db, material, positions) = await SeedAsync(physicalPositions: 1);
        using var _ = db;
        await FillWithOtherMaterialAsync(db, positions);
        var handler = new SubmitInwardWithoutPkmHandler(
            db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser());

        var result = await handler.Handle(
            new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(material.MaterialNumber, 60)], WarehouseId: 1), default);

        var line = Assert.Single(result.Lines);
        Assert.Equal(60, line.AllocatedQuantityBoxes);
        Assert.All(line.AllocatedTo, a => { Assert.False(a.IsConfirmed); Assert.True(a.IsVirtual); });
        Assert.Equal(50, db.StockBatches.Sum(b => b.QuantityBoxes)); // nothing posted: only the other material's stock
        Assert.Equal(2, db.InwardPutaways.Count(p => !p.IsConfirmed));
        // A virtual location with an open put-away is never released.
        var virtuals = TestServices.Virtuals(db);
        foreach (var location in db.PalletPositions.Where(p => p.IsVirtual).ToList())
        {
            Assert.False(await virtuals.ReleaseIfEmptyAsync(location));
        }
    }
}
