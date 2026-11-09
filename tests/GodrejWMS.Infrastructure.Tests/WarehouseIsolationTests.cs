using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Warehouse/region isolation: the EF global query filters must hide other warehouses' data from a scoped user, however
/// a handler queries. Every other test runs as Superadmin, so this is the only place the filters are exercised.</summary>
public class WarehouseIsolationTests
{
    private static async Task<string> SeedAsync()
    {
        var name = Guid.NewGuid().ToString();
        await using var db = TestDbContextFactory.CreateAs(name, "seed", "Superadmin");

        var east = new Region { Code = "E", Name = "East" };
        var west = new Region { Code = "W", Name = "West" };
        db.Regions.AddRange(east, west);
        var eastState = new State { Code = "ES", Name = "East State", Region = east };
        var westState = new State { Code = "WS", Name = "West State", Region = west };
        var eastCity = new City { Code = "EC", Name = "East City", State = eastState };
        var westCity = new City { Code = "WC", Name = "West City", State = westState };
        db.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "W1", Name = "One", City = eastCity },
            new Warehouse { Id = 2, Code = "W2", Name = "Two", City = westCity });
        var material = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 1, WidthMm = 1, HeightMm = 1, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        foreach (var warehouseId in new[] { 1, 2 })
        {
            var rack = new Rack { WarehouseId = warehouseId, Code = $"R{warehouseId}", Columns = 1, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = $"W{warehouseId}-01-01", MaxPallets = 1 });
            db.Racks.Add(rack);
            db.InwardTransactions.Add(new InwardTransaction { WarehouseId = warehouseId, ReferenceNumber = $"GRN-{warehouseId}", CreatedAt = DateTimeOffset.UtcNow });
            db.PulloutTransactions.Add(new PulloutTransaction { WarehouseId = warehouseId, ReferenceNumber = $"PICK-{warehouseId}", CreatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync();
        foreach (var position in db.PalletPositions.IgnoreQueryFilters().ToList())
        {
            db.StockBatches.Add(new StockBatch { MaterialId = material.Id, MfgMonth = 202610, PalletPositionId = position.Id, QuantityBoxes = 10 });
        }

        db.UserWarehouses.Add(new UserWarehouse { UserId = "w1-user", WarehouseId = 1 });
        db.UserRegions.Add(new UserRegion { UserId = "west-manager", RegionId = db.Regions.Single(r => r.Code == "W").Id });
        await db.SaveChangesAsync();
        return name;
    }

    private static async Task AssertSeesOnly(string name, string userId, string role, string expectedReference, string expectedLocation)
    {
        await using var db = TestDbContextFactory.CreateAs(name, userId, role);
        Assert.Equal([expectedReference], await db.InwardTransactions.Select(t => t.ReferenceNumber).ToListAsync());
        Assert.Equal([expectedLocation], await db.PalletPositions.Select(p => p.LocationCode).ToListAsync());
        Assert.Single(await db.Racks.ToListAsync());
        var batch = Assert.Single(await db.StockBatches.ToListAsync());
        Assert.Equal(expectedLocation, (await db.PalletPositions.SingleAsync(p => p.Id == batch.PalletPositionId)).LocationCode);
        Assert.Single(await db.PulloutTransactions.ToListAsync());
    }

    [Fact]
    public async Task WarehouseScopedUser_SeesOnlyTheirOwnWarehouse() =>
        await AssertSeesOnly(await SeedAsync(), "w1-user", "Supervisor", "GRN-1", "W1-01-01");

    [Fact]
    public async Task RegionScopedUser_SeesOnlyWarehousesInTheirRegion() =>
        await AssertSeesOnly(await SeedAsync(), "west-manager", "Logistic Manager", "GRN-2", "W2-01-01");

    [Fact]
    public async Task UserWithNoAssignment_SeesNothing()
    {
        await using var db = TestDbContextFactory.CreateAs(await SeedAsync(), "nobody", "Supervisor");
        Assert.Empty(await db.InwardTransactions.ToListAsync());
        Assert.Empty(await db.PulloutTransactions.ToListAsync());
        Assert.Empty(await db.StockBatches.ToListAsync());
        Assert.Empty(await db.PalletPositions.ToListAsync());
        Assert.Empty(await db.Racks.ToListAsync());
    }

    [Theory]
    [InlineData("Superadmin")]
    [InlineData("Platform Admin")]
    public async Task PlatformLevelUsers_SeeEveryWarehouse(string role)
    {
        await using var db = TestDbContextFactory.CreateAs(await SeedAsync(), "boss", role);
        Assert.Equal(2, await db.InwardTransactions.CountAsync());
        Assert.Equal(2, await db.StockBatches.CountAsync());
    }

    [Fact]
    public async Task ScopedUser_CannotLoadAnotherWarehousesOrderById()
    {
        var name = await SeedAsync();
        int otherId;
        await using (var admin = TestDbContextFactory.CreateAs(name, "boss", "Superadmin"))
        {
            otherId = (await admin.InwardTransactions.SingleAsync(t => t.WarehouseId == 2)).Id;
        }

        await using var db = TestDbContextFactory.CreateAs(name, "w1-user", "Supervisor");
        Assert.Null(await db.InwardTransactions.FirstOrDefaultAsync(t => t.Id == otherId));
    }
}
