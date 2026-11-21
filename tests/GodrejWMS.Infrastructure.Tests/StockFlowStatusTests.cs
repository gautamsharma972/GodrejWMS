using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Application.Features.StockMaster;
using GodrejWMS.Application.Features.StockMaster.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>GIT-IN / IN-STOCK / GIT-OUT on Current Inventory: derived for display only, never changes stock.</summary>
public class StockFlowStatusTests
{
    private static async Task<(AppDbContext Db, Material Mat, List<PalletPosition> Pos)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 4, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 4; c++) rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        db.Racks.Add(rack);
        var mat = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(mat);
        // the grid joins the master data of the stock rows; the test database starts without it
        db.SkuMovementTypes.Add(new SkuMovementType { Id = SkuMovementTypeIds.SlowMoving, Code = "Slow", DisplayName = "Slow" });
        db.Seasons.Add(new Season { Id = SeasonIds.Rainy, Code = "Rainy", DisplayName = "Rainy" });
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Damage, Code = "DAMAGE", DisplayName = "Damage" });
        await db.SaveChangesAsync();
        return (db, mat, rack.PalletPositions.OrderBy(p => p.Column).ToList());
    }

    private static Task UploadInwardAsync(AppDbContext db, decimal quantity, string pkm = "SEP|2026") =>
        new SubmitInwardHandler(db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(40000001, quantity, pkm)], WarehouseId: 1), default);

    private static Task UploadPulloutAsync(AppDbContext db, decimal quantity) =>
        new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, quantity)], WarehouseId: 1), default);

    private static async Task<List<StockFlowRow>> FlowAsync(AppDbContext db, string? status = null) =>
        (await StockFlowRows.LoadAsync(db, new GetStockMasterQuery(WithFlowStatus: true, FlowStatus: status, WarehouseId: 1), default)).ToList();

    private static decimal Book(AppDbContext db) => db.StockBatches.AsNoTracking().Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task InwardUpload_IsGitIn_UntilThePickerConfirms_ThenInStock()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        await UploadInwardAsync(db, 80);

        var rows = await FlowAsync(db);
        Assert.All(rows, r => Assert.Equal(StockFlowStatus.GitIn, r.Status));
        Assert.Equal(80, rows.Sum(r => r.QuantityBoxes));
        Assert.Equal(Book(db), rows.Sum(r => r.QuantityBoxes));          // book total = IN-STOCK + GIT-IN

        foreach (var putaway in db.InwardPutaways.ToList()) putaway.IsPickerPlaced = true;   // the picker submits every location
        await db.SaveChangesAsync();
        var after = await FlowAsync(db);
        Assert.All(after, r => Assert.Equal(StockFlowStatus.InStock, r.Status));
        Assert.Equal(80, after.Sum(r => r.QuantityBoxes));
    }

    [Fact]
    public async Task OnePutawayConfirmed_SplitsTheCells_AndALockedOrRejectedGrnIsInStock()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        await UploadInwardAsync(db, 80);                                 // 50 + 30 in two locations
        var first = db.InwardPutaways.OrderBy(p => p.Id).First();
        first.IsPickerPlaced = true;
        await db.SaveChangesAsync();

        var rows = await FlowAsync(db);
        Assert.Equal(first.QuantityBoxes, rows.Where(r => r.Status == StockFlowStatus.InStock).Sum(r => r.QuantityBoxes));
        Assert.Equal(80 - first.QuantityBoxes, rows.Where(r => r.Status == StockFlowStatus.GitIn).Sum(r => r.QuantityBoxes));

        foreach (var putaway in db.InwardPutaways.ToList()) putaway.IsExcludedLocked = true;       // the GRN is locked / confirmed
        await db.SaveChangesAsync();
        Assert.All(await FlowAsync(db), r => Assert.Equal(StockFlowStatus.InStock, r.Status));

        foreach (var putaway in db.InwardPutaways.ToList()) { putaway.IsExcludedLocked = false; putaway.IsPickerPlaced = false; }
        db.InwardTransactions.Single().IsRejected = true;                                          // a rejected GRN is not GIT-IN
        await db.SaveChangesAsync();
        Assert.All(await FlowAsync(db), r => Assert.Equal(StockFlowStatus.InStock, r.Status));
    }

    [Fact]
    public async Task CellWithOlderStockAndAPendingPutaway_IsSplit_AndGitInNeverExceedsTheCell()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        await UploadInwardAsync(db, 30);                                // posts 30 to one location, put-away pending
        var cell = db.StockBatches.Single();
        cell.QuantityBoxes = 40;                                        // the cell also holds 10 boxes of older stock (same material and PKM)
        await db.SaveChangesAsync();

        var rows = await FlowAsync(db);
        Assert.Equal(10, rows.Single(r => r.Status == StockFlowStatus.InStock).QuantityBoxes);
        Assert.Equal(30, rows.Single(r => r.Status == StockFlowStatus.GitIn).QuantityBoxes);

        cell.QuantityBoxes = 20;                                        // something left the cell since: GIT-IN is capped at what is there
        await db.SaveChangesAsync();
        var capped = await FlowAsync(db);
        Assert.Equal(20, capped.Sum(r => r.QuantityBoxes));
        Assert.Equal(20, capped.Single(r => r.Status == StockFlowStatus.GitIn).QuantityBoxes);
    }

    [Fact]
    public async Task PulloutUpload_ShowsGitOutRows_NotInTheBookTotal_UntilThePickerConfirmsThePick()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await UploadPulloutAsync(db, 25);

        Assert.Equal(15, Book(db));                                      // the books already deducted
        var rows = await FlowAsync(db);
        var gitOut = rows.Single(r => r.Status == StockFlowStatus.GitOut);
        Assert.Equal(25, gitOut.QuantityBoxes);
        Assert.Equal("A-01-01", gitOut.LocationCode);
        Assert.Equal(0, gitOut.StockBatchId);                            // no stock row behind it: the grid offers no Adjust
        Assert.Equal(15, rows.Where(r => r.Status != StockFlowStatus.GitOut).Sum(r => r.QuantityBoxes));
        var totals = await new GetStockFlowTotalsHandler(db).Handle(new GetStockFlowTotalsQuery(new GetStockMasterQuery(WarehouseId: 1)), default);
        Assert.Equal((15m, 0m, 25m), (totals.InStockBoxes, totals.GitInBoxes, totals.GitOutBoxes));

        foreach (var pick in db.PulloutPicks.ToList()) pick.PickedAt = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);   // the picker confirms the pick
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await FlowAsync(db), r => r.Status == StockFlowStatus.GitOut);
        Assert.Equal(15, Book(db));
    }

    [Fact]
    public async Task RejectedPullout_HasNoGitOut()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await UploadPulloutAsync(db, 25);
        db.PulloutTransactions.Single().IsRejected = true;
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await FlowAsync(db), r => r.Status == StockFlowStatus.GitOut);
    }

    [Fact]
    public async Task DamageStockIsAlwaysInStock_FiltersAndSortWork_AndPlainGridIsUnchanged()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[1].Id, MfgMonth = 202609, StockSubtypeId = LocationSubtypeIds.Damage, QuantityBoxes = 5 });
        await db.SaveChangesAsync();
        await UploadInwardAsync(db, 40);

        var all = await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(WithFlowStatus: true, WarehouseId: 1), default);
        Assert.Contains(all.Items, r => r.StockSubtypeName != "Good" && r.StockStatus == "IN-STOCK");
        var gitIn = await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(WithFlowStatus: true, FlowStatus: "GIT-IN", WarehouseId: 1), default);
        Assert.All(gitIn.Items, r => Assert.Equal("GIT-IN", r.StockStatus));
        Assert.Equal(40, gitIn.Items.Sum(r => r.QuantityBoxes));
        var good = await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(WithFlowStatus: true, StockSubtypeId: LocationSubtypeIds.Damage, WarehouseId: 1), default);
        Assert.Equal(5, good.Items.Sum(r => r.QuantityBoxes));
        var sorted = await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(WithFlowStatus: true, SortBy: "quantity", SortDescending: true, WarehouseId: 1), default);
        Assert.Equal(sorted.Items.OrderByDescending(r => r.QuantityBoxes).Select(r => r.QuantityBoxes), sorted.Items.Select(r => r.QuantityBoxes));

        // callers that do not ask for the flow status get the grid exactly as before: one row per stock batch, status IN-STOCK
        var plain = await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(WarehouseId: 1), default);
        Assert.Equal(db.StockBatches.Count(b => b.QuantityBoxes > 0), plain.TotalCount);
        Assert.All(plain.Items, r => Assert.Equal("IN-STOCK", r.StockStatus));
    }

    [Fact]
    public async Task ComputingTheStatus_NeverChangesStockLedgerOrOrders()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await UploadInwardAsync(db, 30);
        await UploadPulloutAsync(db, 10);
        db.ChangeTracker.Clear();
        var stock = db.StockBatches.AsNoTracking().OrderBy(b => b.Id).Select(b => new { b.Id, b.QuantityBoxes, b.RowVersion }).ToList();
        var ledger = db.StockLedgerEntries.Count();
        var putaways = db.InwardPutaways.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.IsPickerPlaced, p.IsConfirmed, p.QuantityBoxes }).ToList();
        var picks = db.PulloutPicks.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.PickedAt, p.QuantityBoxes }).ToList();

        await FlowAsync(db);
        await new GetStockFlowTotalsHandler(db).Handle(new GetStockFlowTotalsQuery(new GetStockMasterQuery(WarehouseId: 1)), default);
        await new GetStockMasterHandler(db).Handle(new GetStockMasterQuery(WithFlowStatus: true), default);

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(stock, db.StockBatches.AsNoTracking().OrderBy(b => b.Id).Select(b => new { b.Id, b.QuantityBoxes, b.RowVersion }).ToList());
        Assert.Equal(ledger, db.StockLedgerEntries.Count());
        Assert.Equal(putaways, db.InwardPutaways.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.IsPickerPlaced, p.IsConfirmed, p.QuantityBoxes }).ToList());
        Assert.Equal(picks, db.PulloutPicks.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.PickedAt, p.QuantityBoxes }).ToList());
    }

    private sealed class AllowReports : GodrejWMS.Application.Common.Permissions.IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class OneWarehouse : GodrejWMS.Application.Common.Interfaces.IWarehouseAccessService
    {
        public Task<IReadOnlyList<GodrejWMS.Application.Common.Interfaces.WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GodrejWMS.Application.Common.Interfaces.WarehouseOption>>([]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task MaterialStockReport_KeepsTheBookTotal_AndAddsInStockGitInAndGitOut()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await UploadInwardAsync(db, 30, "OCT|2026");
        await UploadPulloutAsync(db, 25);                               // FIFO takes the older SEP stock
        var handler = new GetMaterialStockReportHandler(db, new OneWarehouse(), new AllowReports());

        var rows = await handler.Handle(new GetMaterialStockReportQuery(new MaterialStockReportFilter(WarehouseId: 1)), default);

        var oct = rows.Single(r => r.Pkm == 202610);
        Assert.Equal(30, oct.BoxQuantity);
        Assert.Equal(0, oct.InStockBoxes);
        Assert.Equal(30, oct.GitInBoxes);
        var sep = rows.Single(r => r.Pkm == 202609);
        Assert.Equal(15, sep.BoxQuantity);                              // book total after the pullout upload
        Assert.Equal(15, sep.InStockBoxes);
        Assert.Equal(25, sep.GitOutBoxes);                              // still on the rack, not in the total
        Assert.Equal(Book(db), rows.Sum(r => r.BoxQuantity));           // the report total still equals the book stock
        Assert.All(rows, r => Assert.Equal(r.BoxQuantity, r.InStockBoxes + r.GitInBoxes));

        var onlyOut = await handler.Handle(new GetMaterialStockReportQuery(new MaterialStockReportFilter(WarehouseId: 1, Status: "GIT-OUT")), default);
        Assert.Equal(202609, Assert.Single(onlyOut).Pkm);
    }

    [Fact]
    public async Task MaterialStockReport_ListsAMaterialWhoseWholeStockIsInUnconfirmedPicks()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await UploadPulloutAsync(db, 40);                               // the books are now empty, the boxes are still on the rack

        var rows = await new GetMaterialStockReportHandler(db, new OneWarehouse(), new AllowReports())
            .Handle(new GetMaterialStockReportQuery(new MaterialStockReportFilter(WarehouseId: 1)), default);

        var row = Assert.Single(rows);
        Assert.Equal((0m, 0m, 0m, 40m), (row.BoxQuantity, row.InStockBoxes, row.GitInBoxes, row.GitOutBoxes));
    }

    [Fact]
    public async Task LocationStockReport_ShowsTheStatusSplit_AndALocationHoldingOnlyGitOutIsNotEmpty()
    {
        var (db, mat, pos) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = mat.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 40 });
        await db.SaveChangesAsync();
        await UploadPulloutAsync(db, 40);                               // A-01-01: no stock in the books, 40 boxes of GIT-OUT
        await UploadInwardAsync(db, 20, "OCT|2026");                    // another location: 20 GIT-IN
        var handler = new GetLocationStockReportHandler(db, new OneWarehouse(), new AllowReports());

        var rows = await handler.Handle(new GetLocationStockReportQuery(new LocationStockReportFilter(WarehouseId: 1)), default);

        var outRow = rows.Single(r => r.GitOutQty > 0);
        Assert.Equal("A-01-01", outRow.LocationCode);
        Assert.NotNull(outRow.MaterialCode);                            // not shown as "Empty": the pallet is still there
        Assert.Equal((0m, 40m), (outRow.StockQty, outRow.GitOutQty));
        var inbound = rows.Single(r => r.GitInQty > 0);
        Assert.Equal((20m, 0m, 20m), (inbound.StockQty, inbound.InStockQty, inbound.GitInQty));
        Assert.Equal(Book(db), rows.Sum(r => r.StockQty));              // totals are the book stock
        Assert.Equal(rows.Count(r => r.MaterialCode is null), rows.Select(r => r.LocationCode).Distinct().Count() - rows.Where(r => r.MaterialCode is not null).Select(r => r.LocationCode).Distinct().Count());

        var onlyGitIn = await handler.Handle(new GetLocationStockReportQuery(new LocationStockReportFilter(WarehouseId: 1, Status: "GIT-IN")), default);
        Assert.All(onlyGitIn, r => Assert.True(r.GitInQty > 0));
    }
}
