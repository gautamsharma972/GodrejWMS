using ClosedXML.Excel;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Locations.Queries;
using GodrejWMS.Application.Features.StockMaster.Commands;
using GodrejWMS.Application.Features.StockMaster.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class MixedLocationInventoryImportTests
{
    private static async Task<(AppDbContext Db, Material A, Material B, PalletPosition Position)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { WarehouseId = 1, Code = "M", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "M-01-01", MaxPallets = 1 });
        db.Racks.Add(rack);
        Material Make(long number, int palletSize) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = palletSize, SeasonId = SeasonIds.Rainy
        };
        var a = Make(40000001, 54);
        var b = Make(40000002, 24);
        db.Materials.AddRange(a, b);
        await db.SaveChangesAsync();
        return (db, a, b, rack.PalletPositions.Single());
    }

    private static MemoryStream Workbook(params (long Code, decimal Qty, string Pkm, string Location)[] rows) =>
        Workbook(rows.Select(r => ((object)r.Code, r.Qty, r.Pkm, r.Location)).ToArray());

    private static MemoryStream Workbook(params (object Code, decimal Qty, string Pkm, string Location)[] rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Inventory Master");
        string[] headers = ["Material Code", "Active", "Total Stock in CFB", "Mfg Month", "Pallet Position"];
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
        {
            ws.Cell(r + 2, 1).Value = XLCellValue.FromObject(rows[r].Code);
            ws.Cell(r + 2, 2).Value = "Active";
            ws.Cell(r + 2, 3).Value = rows[r].Qty;
            ws.Cell(r + 2, 4).Value = rows[r].Pkm;
            ws.Cell(r + 2, 5).Value = rows[r].Location;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static Task<ImportStockMasterResult> ImportAsync(AppDbContext db, MemoryStream file, int? warehouseId = 1) =>
        new ImportStockMasterFromExcelHandler(db, new ClosedXmlExcelService(), new MixClock(), new MixUser(), new MixAllow(), new MixWarehouses())
            .Handle(new ImportStockMasterFromExcelCommand(file, warehouseId), default);

    [Fact]
    public async Task SeveralMaterialsAndPkmsInOneLocation_AreAllLoaded()
    {
        var (db, _, _, _) = await SeedAsync();
        using var _ = db;

        var result = await ImportAsync(db, Workbook(
            (40000001, 10, "MAR|2026", "M-01-01"),
            (40000001, 5, "APR|2026", "M-01-01"),
            (40000002, 4, "MAR|2026", "M-01-01")));

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings!);
        Assert.Equal(3, result.Created);
        Assert.Equal(3, db.StockBatches.Count());
    }

    [Fact]
    public async Task TotalAboveTheSmallestPalletSize_IsLoadedWithAWarning_InAnyRowOrder()
    {
        // Capacity of the location is 1 x min(54, 24) = 24; 30 + 8 = 38 boxes is over it.
        var rows = new (long, decimal, string, string)[]
        {
            (40000001, 30, "MAR|2026", "M-01-01"),
            (40000002, 8, "MAR|2026", "M-01-01")
        };
        foreach (var ordered in new[] { rows, rows.Reverse().ToArray() })
        {
            var (db, _, _, _) = await SeedAsync();
            using var _ = db;
            var result = await ImportAsync(db, Workbook(ordered));

            Assert.Empty(result.Errors);
            Assert.Equal(2, db.StockBatches.Count());
            var warning = Assert.Single(result.Warnings!);
            Assert.Contains("M-01-01", warning);
            Assert.Contains("24", warning);
        }
    }

    [Fact]
    public async Task DuplicateMaterialPkmAndLocationInOneFile_IsReported_AndTheOtherRowsLoad()
    {
        var (db, _, _, _) = await SeedAsync();
        using var _ = db;

        var result = await ImportAsync(db, Workbook(
            (40000001, 10, "MAR|2026", "M-01-01"),
            (40000001, 12, "MAR|2026", "M-01-01"),
            (40000002, 4, "MAR|2026", "M-01-01")));

        var error = Assert.Single(result.Errors);
        Assert.Contains("Row 3", error);
        Assert.Contains("row 2", error);
        Assert.Equal(2, db.StockBatches.Count());
        Assert.Equal(10, db.StockBatches.Single(b => b.Material.MaterialNumber == 40000001).QuantityBoxes);
    }

    [Fact]
    public async Task Upload_ReplacesTheWarehouseStock_InsteadOfMergingWithIt()
    {
        var (db, a, b, position) = await SeedAsync();
        using var _ = db;
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = a.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 10 },
            new StockBatch { MaterialId = b.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 4 });
        await db.SaveChangesAsync();

        // The file lists only material A with a new quantity: A is replaced, B (not in the file) is cleared.
        var result = await ImportAsync(db, Workbook((40000001, 7, "MAR|2026", "M-01-01")));

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Cleared);
        Assert.Equal(7, db.StockBatches.Single(x => x.MaterialId == a.Id).QuantityBoxes);
        Assert.Equal(0, db.StockBatches.Single(x => x.MaterialId == b.Id).QuantityBoxes);
    }

    [Fact]
    public async Task InvalidRows_AreSkippedAndReported_WhileCorrectRowsAreLoaded()
    {
        var (db, a, _, position) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = a.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 10 });
        await db.SaveChangesAsync();

        // Row 3 is an unknown code and row 4 a malformed one ("40000002x"), which used to end the sheet silently.
        var result = await ImportAsync(db, Workbook(
            ((object)40000001L, 7m, "MAR|2026", "M-01-01"),
            (49999999L, 3m, "MAR|2026", "M-01-01"),
            ("40000002x", 3m, "MAR|2026", "M-01-01")));

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("Row 3", result.Errors[0]);
        Assert.Contains("Row 4", result.Errors[1]);
        Assert.Contains("40000002x", result.Errors[1]);
        Assert.Equal(1, result.Updated);
        Assert.Equal(7, Assert.Single(db.StockBatches).QuantityBoxes);
    }

    [Fact]
    public async Task OnlyTheLocationsInTheFileAreReplaced()
    {
        var (db, a, b, position) = await SeedAsync();
        using var _ = db;
        var otherRack = new Rack { WarehouseId = 1, Code = "N", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        otherRack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "N-01-01", MaxPallets = 1 });
        db.Racks.Add(otherRack);
        await db.SaveChangesAsync();
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = a.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 10 },
            new StockBatch { MaterialId = b.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 4 },
            new StockBatch { MaterialId = a.Id, PalletPositionId = otherRack.PalletPositions.Single().Id, MfgMonth = 202603, QuantityBoxes = 20 });
        await db.SaveChangesAsync();

        var result = await ImportAsync(db, Workbook((40000001, 7, "MAR|2026", "M-01-01")));

        Assert.Equal(1, result.Cleared);
        Assert.Equal(20, db.StockBatches.Single(x => x.PalletPosition.LocationCode == "N-01-01").QuantityBoxes); // not in the file: untouched
        Assert.Equal(0, db.StockBatches.Single(x => x.MaterialId == b.Id).QuantityBoxes); // same location, not in the file: cleared
    }

    [Fact]
    public async Task LocationOfAnotherWarehouse_IsAnError_AndAnEmptyFileChangesNothing()
    {
        var (db, a, _, position) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = a.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 10 });
        await db.SaveChangesAsync();

        var otherWarehouse = await ImportAsync(db, Workbook((40000001, 7, "MAR|2026", "M-01-01")), warehouseId: 99);
        Assert.Contains("different warehouse", Assert.Single(otherWarehouse.Errors));

        var empty = await ImportAsync(db, Workbook(Array.Empty<(object, decimal, string, string)>()));
        Assert.Contains("no stock rows", Assert.Single(empty.Errors));
        Assert.Equal(10, Assert.Single(db.StockBatches).QuantityBoxes);
    }

    [Fact]
    public async Task InventoryMasterSearch_FindsStockByPalletPosition()
    {
        var (db, a, b, position) = await SeedAsync();
        using var _ = db;
        var other = new PalletPosition { RackId = position.RackId, Column = 2, Level = 1, LocationCode = "M-02-01", MaxPallets = 1 };
        db.PalletPositions.Add(other);
        db.SkuMovementTypes.Add(new SkuMovementType { Id = SkuMovementTypeIds.SlowMoving, Code = "Slow", DisplayName = "Slow" });
        db.Seasons.Add(new Season { Id = SeasonIds.Rainy, Code = "Rainy", DisplayName = "Rainy" });
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        await db.SaveChangesAsync();
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = a.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 10 },
            new StockBatch { MaterialId = b.Id, PalletPositionId = other.Id, MfgMonth = 202603, QuantityBoxes = 4 });
        await db.SaveChangesAsync();

        var result = await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(MaterialSearch: "M-02-01"), default);

        var row = Assert.Single(result.Items);
        Assert.Equal("M-02-01", row.PalletPositionCode);
        Assert.Equal(40000002, row.MaterialNumber);
    }

    [Fact]
    public async Task LocationCapacity_UsesTheSmallestPalletSizeOfTheMaterialsKeptThere()
    {
        var (db, a, b, position) = await SeedAsync();
        using var _ = db;
        // GetLocations joins the master tables, which the in-memory test database does not seed.
        db.LocationTypes.Add(new LocationType { Id = LocationTypeIds.Rack, Code = "RACK", DisplayName = "Rack" });
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = a.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 10 },
            new StockBatch { MaterialId = b.Id, PalletPositionId = position.Id, MfgMonth = 202603, QuantityBoxes = 4 });
        await db.SaveChangesAsync();

        Assert.Equal(24, PalletCapacityCalculator.EffectiveCapacityBoxes(1, new[] { 54, 24 }));

        var locations = await new GetLocationsHandler(db).Handle(new GetLocationsQuery(), default);
        var location = locations.Items.Single(l => l.LocationCode == "M-01-01");
        Assert.Equal(24, location.CapacityBoxes);
        Assert.Equal(14, location.OccupiedBoxes);
    }
}

file sealed class MixWarehouses : IWarehouseAccessService
{
    public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WarehouseOption>>([new WarehouseOption(1, "W", "Warehouse")]);

    public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file sealed class MixAllow : IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file sealed class MixUser : ICurrentUserService
{
    public string? UserId => "test-user";
    public string? UserName => "test@godrejwms.local";
    public bool IsInRole(string role) => true;
    public IReadOnlyList<string> Roles => ["Superadmin"];
}

file sealed class MixClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
