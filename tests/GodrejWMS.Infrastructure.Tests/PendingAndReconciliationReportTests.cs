using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PendingAndReconciliationReportTests
{
    private sealed class AllowAll : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class OneWarehouse : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([new WarehouseOption(1, "GDR1", "Godrej 1")]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class LaterClock(TimeSpan offset) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => TestServices.Now + offset;
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 3; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        db.Materials.Add(new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        });
        await db.SaveChangesAsync();
        await new SubmitInwardHandler(
                db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(40000001, 30, "OCT|2026")], WarehouseId: 1), default);
        return db;
    }

    private static Task PullOutAsync(AppDbContext db, decimal quantity) =>
        new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, quantity)], WarehouseId: 1), default);

    private static GetPendingConfirmationReportHandler Pending(AppDbContext db, TimeSpan? later = null) =>
        new(db, new AllowAll(), later is null ? new SvcClock() : new LaterClock(later.Value));

    private static GetStockReconciliationReportHandler Reconcile(AppDbContext db) => new(db, new AllowAll(), new OneWarehouse());

    [Fact]
    public async Task PendingReport_ListsGrnAndPullout_AndFlagsTheOnesWaitingTooLong()
    {
        var db = await SeedAsync();
        using var _ = db;
        await PullOutAsync(db, 10);

        var fresh = await Pending(db).Handle(new GetPendingConfirmationReportQuery(), default);
        Assert.Equal(2, fresh.Rows.Count);
        var grn = fresh.Rows.Single(r => r.Kind == "GRN");
        Assert.Equal(20, grn.WaitingBoxes);            // 10 of the 30 boxes became PICK-RESERVED, 20 still to place
        Assert.Equal(10, grn.PickReservedBoxes);
        Assert.Equal(10, fresh.Rows.Single(r => r.Kind == "Pullout").WaitingBoxes);
        Assert.Equal(0, fresh.OverdueCount);

        var later = await Pending(db, TimeSpan.FromHours(30)).Handle(new GetPendingConfirmationReportQuery(OverdueHours: 24), default);
        Assert.Equal(2, later.OverdueCount);
    }

    [Fact]
    public async Task PendingReport_DropsAGrnOnceThePickerConfirmedEveryLocation()
    {
        var db = await SeedAsync();
        using var _ = db;
        var putaway = await db.InwardPutaways.SingleAsync();
        putaway.IsPickerPlaced = true;
        putaway.IsExcludedLocked = true;
        await db.SaveChangesAsync();

        var report = await Pending(db).Handle(new GetPendingConfirmationReportQuery(), default);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public async Task Reconciliation_IsClean_AfterNormalInwardAndPullout()
    {
        var db = await SeedAsync();
        using var _ = db;
        await PullOutAsync(db, 10);

        var report = await Reconcile(db).Handle(new GetStockReconciliationReportQuery(), default);

        Assert.DoesNotContain(report.Issues, i => i.Severity != "Info");
        Assert.True(report.BatchesChecked > 0);
    }

    [Fact]
    public async Task Reconciliation_FlagsStockChangedWithoutALedgerEntry_AndPutawayWithoutStock()
    {
        var db = await SeedAsync();
        using var _ = db;
        var batch = await db.StockBatches.SingleAsync();
        batch.QuantityBoxes -= 8; // a silent change nobody posted
        await db.SaveChangesAsync();

        var report = await Reconcile(db).Handle(new GetStockReconciliationReportQuery(), default);

        var ledger = Assert.Single(report.Issues, i => i.Check == ReconciliationChecks.LedgerBalance);
        Assert.Equal(30, ledger.Expected);
        Assert.Equal(22, ledger.Actual);
        Assert.Equal(-8, ledger.Difference);
        var missing = Assert.Single(report.Issues, i => i.Check == ReconciliationChecks.MissingStock);
        Assert.Equal(30, missing.Expected);
        Assert.Equal(22, missing.Actual);
    }

    [Fact]
    public async Task Reconciliation_FlagsAGrnLineWithMoreBoxesPutAwayThanReceived()
    {
        var db = await SeedAsync();
        using var _ = db;
        var putaway = await db.InwardPutaways.SingleAsync();
        putaway.QuantityBoxes = 45; // received 30
        await db.SaveChangesAsync();

        var report = await Reconcile(db).Handle(new GetStockReconciliationReportQuery(), default);

        var issue = Assert.Single(report.Issues, i => i.Check == ReconciliationChecks.OverAllocated);
        Assert.Equal(30, issue.Expected);
        Assert.Equal(45, issue.Actual);
    }
}
