using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.StockMaster.Commands;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class AddStockRecordTests
{
    private sealed class AllWarehouses : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static async Task<(Persistence.AppDbContext Db, AddStockRecordHandler Handler)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 });
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
        return (db, new AddStockRecordHandler(db, TestServices.Posting(db), TestServices.Uow(db), new AllWarehouses()));
    }

    [Fact]
    public async Task Adds_ToALocationThatAlreadyHoldsTheSameMaterialAndPkm()
    {
        var (db, handler) = await SeedAsync();
        using var _ = db;

        await handler.Handle(new AddStockRecordCommand(40000001, "A-01-01", "NOV|2026", 10), default);
        var second = await handler.Handle(new AddStockRecordCommand(40000001, "A-01-01", "NOV|2026", 5), default);

        Assert.Equal(15, second.NewQuantityBoxes);
        Assert.Equal(15, db.StockBatches.AsNoTracking().Single().QuantityBoxes);
        Assert.Equal(2, db.StockLedgerEntries.AsNoTracking().Count());
    }

    [Fact]
    public async Task Adds_AnotherMaterialToTheSameLocation_AndWarnsWhenOverCapacity()
    {
        var (db, handler) = await SeedAsync();
        using var _ = db;
        await handler.Handle(new AddStockRecordCommand(40000001, "A-01-01", "NOV|2026", 30), default);

        var result = await handler.Handle(new AddStockRecordCommand(40000002, "A-01-01", "NOV|2026", 30), default);

        Assert.Equal(2, db.StockBatches.AsNoTracking().Count());
        Assert.NotNull(result.Warning); // 60 boxes in a 40-box location: added, but reported
    }

    [Fact]
    public async Task Rejects_UnknownMaterial_UnknownLocation_AndBadMonth()
    {
        var (db, handler) = await SeedAsync();
        using var _ = db;

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new AddStockRecordCommand(999, "A-01-01", "NOV|2026", 1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new AddStockRecordCommand(40000001, "Z-99-99", "NOV|2026", 1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new AddStockRecordCommand(40000001, "A-01-01", "garbage", 1), default));
        Assert.Empty(db.StockBatches);
    }
}
