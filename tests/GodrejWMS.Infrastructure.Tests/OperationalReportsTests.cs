using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
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
        var dbName = Guid.NewGuid().ToString();
        using var db = Create(new User("admin", true), dbName);
        SeedStock(db);
        var handler = new GetOperationalReportHandler(db, new DisplayNames(), WarehouseAccess(new User("admin", true), dbName), new AlwaysGrantedPermissions());

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
    public async Task Supervisor_IsScopedToTheirAssignedWarehouse()
    {
        var dbName = Guid.NewGuid().ToString();
        var user = new User("supervisor", false);
        using var db = Create(user, dbName);
        SeedStock(db);
        db.UserWarehouses.Add(new UserWarehouse { UserId = user.UserId!, WarehouseId = 1 });
        await db.SaveChangesAsync();
        var handler = new GetOperationalReportHandler(db, new DisplayNames(), WarehouseAccess(user, dbName), new AlwaysGrantedPermissions());

        Assert.Equal(2, (await handler.Handle(new(ReportKind.CurrentStock, new(), Sort: "sku"), default)).TotalCount);
        Assert.Equal(2, (await handler.Handle(new(ReportKind.CurrentStock, new(WarehouseId: 1), Sort: "sku"), default)).TotalCount);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new(ReportKind.CurrentStock, new(WarehouseId: 2), Sort: "sku"), default));
    }

    [Fact]
    public async Task Operator_CanExecuteReportQuery()
    {
        var dbName = Guid.NewGuid().ToString();
        var user = new User("operator", false, false);
        using var db = Create(user, dbName);
        var result = await new GetOperationalReportHandler(db, new DisplayNames(), WarehouseAccess(user, dbName), new AlwaysGrantedPermissions())
            .Handle(new(ReportKind.CurrentStock, new()), default);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task AvailableLocations_ReturnsOnlyActiveLocationsWithoutStockOrPendingPutaway()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = Create(new User("admin", true), dbName);
        SeedStock(db);
        var rack = db.Racks.Single(r => r.WarehouseId == 1);
        rack.PalletPositions.Add(new PalletPosition
        {
            LocationCode = "A-03-01",
            Column = 3,
            Level = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var handler = new GetOperationalReportHandler(
            db,
            new DisplayNames(),
            WarehouseAccess(new User("admin", true), dbName),
            new AlwaysGrantedPermissions());

        var result = await handler.Handle(
            new(ReportKind.AvailableLocations, new(), Sort: "location"), default);

        var location = Assert.Single(result.Items);
        Assert.Equal("A-03-01", location.Location);
        Assert.Equal("Empty", location.Status);
    }

    private static AppDbContext Create(User user, string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        var db = new AppDbContext(options, user, new Clock());
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>A real WarehouseAccessService, backed by a factory that opens a fresh AppDbContext
    /// against the same named in-memory database as the test's own context - mirrors how the real
    /// ApplicationDbContextFactory gives it an isolated context, see WarehouseAccessService's own
    /// doc comment for why.</summary>
    private static IWarehouseAccessService WarehouseAccess(User user, string dbName) =>
        new WarehouseAccessService(new TestDbContextFactory(dbName), user);

    private sealed class TestDbContextFactory(string dbName) : IApplicationDbContextFactory
    {
        public Task<IApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
            return Task.FromResult<IApplicationDbContext>(new AppDbContext(options, new NoUser(), new Clock()));
        }
    }

    private sealed class NoUser : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class DisplayNames : IUserDisplayNameService
    {
        public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.Distinct().ToDictionary(id => id, id => $"User {id}"));
    }

    private static void SeedStock(AppDbContext db)
    {
        db.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "GCPL", Name = "GCPL" },
            new Warehouse { Id = 2, Code = "OTHER", Name = "Other" });
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
        public bool IsInRole(string role) => role == "Superadmin" ? admin :
            role == "Supervisor" ? supervisor : role == "Office Staff" && !admin && !supervisor;
        public IReadOnlyList<string> Roles =>
            [admin ? "Superadmin" : supervisor ? "Supervisor" : "Office Staff"];
    }
    private sealed class Clock : IDateTimeProvider
    { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow); }

    private sealed class AlwaysGrantedPermissions : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
