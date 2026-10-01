using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Core;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Tests;

public class OperationalReportsTests
{
    [Fact]
    public async Task CurrentStock_FiltersAndPaginates_ThenSkuReportAggregates()
    {
        using var db = Create(new User("admin", true));
        SeedStock(db);
        var handler = new GetOperationalReportHandler(db, new User("admin", true), new DisplayNames());

        var page = await handler.Handle(new(ReportKind.CurrentStock,
            new ReportFilter(WarehouseId: 1, Sku: 1001), PageSize: 1, Sort: "quantity", Descending: true), default);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(8, page.Items[0].Quantity);

        var summary = await handler.Handle(new(ReportKind.SkuStock, new(), Sort: "sku"), default);
        var sku = Assert.Single(summary.Items, r => r.Sku == 1001);
        Assert.Equal(13, sku.Quantity);
        Assert.Equal(2, sku.Locations);
        Assert.Equal(2, sku.PkmVariants);
    }

    [Fact]
    public async Task Supervisor_CanRequestAnyWarehouse()
    {
        var user = new User("supervisor", false);
        using var db = Create(user);
        SeedStock(db);
        db.UserWarehouses.Add(new UserWarehouse { UserId = user.UserId!, WarehouseId = 1 });
        await db.SaveChangesAsync();
        var handler = new GetOperationalReportHandler(db, user, new DisplayNames());

        Assert.Equal(2, (await handler.Handle(new(ReportKind.CurrentStock, new(), Sort: "sku"), default)).TotalCount);
        var otherWarehouse = await handler.Handle(
            new(ReportKind.CurrentStock, new(WarehouseId: 2), Sort: "sku"), default);
        Assert.Equal(0, otherWarehouse.TotalCount);
    }

    [Fact]
    public async Task Operator_CanExecuteReportQuery()
    {
        var user = new User("operator", false, false);
        using var db = Create(user);
        var result = await new GetOperationalReportHandler(db, user, new DisplayNames())
            .Handle(new(ReportKind.CurrentStock, new()), default);
        Assert.Equal(0, result.TotalCount);
    }

    private static AppDbContext Create(User user)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, user, new Clock());
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class DisplayNames : IUserDisplayNameService
    {
        public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.Distinct().ToDictionary(id => id, id => $"User {id}"));
    }

    private static void SeedStock(AppDbContext db)
    {
        db.Warehouses.Add(new Warehouse { Id = 2, Code = "OTHER", Name = "Other" });
        db.LocationTypes.Add(new LocationType { Id = 1, Code = "Rack", DisplayName = "Rack" });
        db.LocationSubtypes.Add(new LocationSubtype { Id = 1, Code = "Good", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = 2, Code = "Reserve", DisplayName = "Reserve" });
        db.Seasons.Add(new Season { Id = 1, Code = "Summer", DisplayName = "Summer" });
        db.SkuMovementTypes.Add(new SkuMovementType { Id = 2, Code = "Slow", DisplayName = "Slow" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 2, Levels = 1 };
        var a = new PalletPosition { LocationCode = "A-01-01", Column = 1, Level = 1 };
        var b = new PalletPosition { LocationCode = "A-02-01", Column = 2, Level = 1 };
        rack.PalletPositions.Add(a); rack.PalletPositions.Add(b); db.Racks.Add(rack);
        var material = new Material { MaterialNumber = 1001, Description = "SKU", DesignType = "D", SeasonId = 1, MovementTypeId = 2 };
        db.Materials.Add(material); db.SaveChanges();
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = material.Id, PalletPositionId = a.Id, MfgMonth = 202601, QuantityBoxes = 5 },
            new StockBatch { MaterialId = material.Id, PalletPositionId = b.Id, MfgMonth = 202602, QuantityBoxes = 8 });
        db.SaveChanges();
    }

    private sealed class User(string id, bool admin, bool supervisor = true) : ICurrentUserService
    {
        public string? UserId => id; public string? UserName => id;
        public bool IsInRole(string role) => role == "Admin" ? admin :
            role == "Supervisor" ? supervisor : role == "Operator" && !admin && !supervisor;
    }
    private sealed class Clock : IDateTimeProvider
    { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow); }
}
