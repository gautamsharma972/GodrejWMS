using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class InwardVarianceTests
{
    private static async Task<(AppDbContext Db, List<PalletPosition> Positions, Material Material)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 4, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 4; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.Materials.Add(new Material
        {
            MaterialNumber = 40000002, Description = "Other", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        });
        await db.SaveChangesAsync();
        await new SubmitInwardHandler(
                db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(material.MaterialNumber, 30, "OCT|2026")], WarehouseId: 1), default);
        return (db, rack.PalletPositions.OrderBy(p => p.Column).ToList(), material);
    }

    private static RecordInwardVarianceHandler Record(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());

    private static RecordExcessMaterialHandler Excess(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());

    private static decimal Stock(AppDbContext db, int subtype) =>
        db.StockBatches.AsNoTracking().Where(b => b.StockSubtypeId == subtype).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task Damage_StaysInTheSameCell_ButBecomesDamageStock_AndTheRowShrinks()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();

        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 5, "Wet cartons"), default);

        Assert.Equal(25, Stock(db, LocationSubtypes.Good));
        Assert.Equal(5, Stock(db, LocationSubtypes.Damage));
        var batches = db.StockBatches.AsNoTracking().ToList();
        Assert.Equal(2, batches.Count);
        Assert.Single(batches.Select(b => b.PalletPositionId).Distinct()); // same cell
        Assert.Equal(25, db.InwardPutaways.AsNoTracking().Single().QuantityBoxes);
        var line = db.InwardTransactionLines.AsNoTracking().Single();
        Assert.Equal(25, line.AllocatedQuantityBoxes);
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Fulfilled, line.Status); // 25 put away + 5 damaged = 30 inbound
        Assert.Contains(db.StockLedgerEntries.AsNoTracking(), e => e.EntryType == StockLedgerEntryTypes.DamageReclass && e.ReferenceType == "GRN_VARIANCE");
    }

    [Fact]
    public async Task TheQuantityCap_CountsRecords_SoPutawayPlusRecordsNeverExceedTheInbound()
    {
        var (db, positions, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Shortage, 5, "Not on the truck"), default);
        var place = new PickerPlaceInwardPutawayHandler(db, TestServices.Posting(db), TestServices.Git(db), TestServices.Uow(db), new AllowAllPermissions(), new SvcUser(), new SvcClock());

        var tooMany = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            place.Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 26, "OCT|2026", putaway.PalletPositionId, null, AcknowledgeWarning: true), default));
        Assert.Contains("more than the 30", tooMany.Message);
        Assert.True((await place.Handle(
            new PickerPlaceInwardPutawayCommand(putaway.Id, 25, "OCT|2026", putaway.PalletPositionId, null, AcknowledgeWarning: true), default)).Saved);
    }

    [Fact]
    public async Task ShortageAndMismatch_LeaveGoodStock_WithoutDamageStock_AndRefreshPutawayNeverRefillsThem()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Shortage, 4, "Short"), default);
        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Mismatch, 6, "Wrong pack"), default);

        Assert.Equal(20, Stock(db, LocationSubtypes.Good));
        Assert.Equal(0, Stock(db, LocationSubtypes.Damage));

        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        var refresh = new RefreshPostedInwardPutawayHandler(
            db, new PalletAllocationService(db, new SvcClock()), TestServices.Posting(db), TestServices.Virtuals(db), TestServices.Uow(db), new SvcClock(), new SvcUser());
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh.Handle(new RefreshPostedInwardPutawayCommand(transactionId), default));
        Assert.Equal(20, Stock(db, LocationSubtypes.Good));

        var detail = (await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(transactionId), default));
        var line = detail.Lines.Single();
        Assert.Equal(10, line.VarianceBoxes);
        Assert.Equal(0, line.UnplacedBoxes);
        Assert.Equal(0, line.LeftInventoryBoxes);
        Assert.Equal(2, detail.Variances!.Count);
    }

    [Fact]
    public async Task Excess_IsPostedToTheChosenLocation_OutsideTheCap_AndLeavesTheLineAlone()
    {
        var (db, positions, material) = await SeedAsync();
        using var _ = db;
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        var other = db.Materials.AsNoTracking().Single(m => m.MaterialNumber == 40000002);
        var target = positions.Last();

        var result = await Excess(db).Handle(new RecordExcessMaterialCommand(transactionId, other.Id, "OCT|2026", target.Id, 12, "Found on the vehicle"), default);

        Assert.True(result.Saved);
        Assert.Equal(42, Stock(db, LocationSubtypes.Good)); // 30 inbound + 12 excess
        var line = db.InwardTransactionLines.AsNoTracking().Single();
        Assert.Equal(30, line.RequestedQuantityBoxes);
        Assert.Equal(30, line.AllocatedQuantityBoxes);
        Assert.Contains(db.StockBatches.AsNoTracking(), b => b.MaterialId == other.Id && b.PalletPositionId == target.Id && b.QuantityBoxes == 12);
        Assert.Contains(db.StockLedgerEntries.AsNoTracking(), e => e.EntryType == StockLedgerEntryTypes.ExcessReceived && e.QuantityBoxes == 12);

        // The material put-away is unaffected: still 30 and nothing to refresh or reconcile.
        var detail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(transactionId), default);
        Assert.Equal(0, detail.Lines.Single().UnplacedBoxes);
        Assert.Equal(1, detail.Variances!.Count);
    }

    [Fact]
    public async Task Excess_WarnsAboutAnOccupiedLocation_UntilAcknowledged()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        var other = db.Materials.AsNoTracking().Single(m => m.MaterialNumber == 40000002);
        var occupied = db.InwardPutaways.AsNoTracking().Single().PalletPositionId; // holds the GRN's own material

        var first = await Excess(db).Handle(new RecordExcessMaterialCommand(transactionId, other.Id, "OCT|2026", occupied, 5, "x"), default);
        Assert.False(first.Saved);
        Assert.Contains("already holds", first.Warning);
        Assert.Equal(30, Stock(db, LocationSubtypes.Good));

        var second = await Excess(db).Handle(new RecordExcessMaterialCommand(transactionId, other.Id, "OCT|2026", occupied, 5, "x", AcknowledgeWarning: true), default);
        Assert.True(second.Saved);
        Assert.Equal(35, Stock(db, LocationSubtypes.Good));
    }

    [Fact]
    public async Task RemovingARecord_ReversesItsStockEffect_AndGivesTheBoxesBackToTheRow()
    {
        var (db, positions, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        var other = db.Materials.AsNoTracking().Single(m => m.MaterialNumber == 40000002);
        var damage = await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 5, "Wet"), default);
        var excess = await Excess(db).Handle(new RecordExcessMaterialCommand(transactionId, other.Id, "OCT|2026", positions.Last().Id, 7, "x"), default);

        var remove = new RemoveInwardVarianceHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());
        await remove.Handle(new RemoveInwardVarianceCommand(damage.Id), default);
        await remove.Handle(new RemoveInwardVarianceCommand(excess.Record!.Id), default);

        Assert.Equal(30, Stock(db, LocationSubtypes.Good));
        Assert.Equal(0, Stock(db, LocationSubtypes.Damage));
        Assert.Equal(30, db.InwardPutaways.AsNoTracking().Single().QuantityBoxes);
        Assert.Equal(30, db.InwardTransactionLines.AsNoTracking().Single().AllocatedQuantityBoxes);
        Assert.Empty(db.InwardLineVariances.AsNoTracking());
    }

    [Fact]
    public async Task ARecordIsRefused_OnceTheSupervisorHasConfirmedTheGrn()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var putaway = await db.InwardPutaways.SingleAsync();
        putaway.IsPickerPlaced = true;
        putaway.IsExcludedLocked = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 1, "late"), default));
        Assert.Contains("already been confirmed", ex.Message);
    }

    [Fact]
    public async Task RejectingTheGrn_TakesBackItsDamageStockAndExcessMaterialToo()
    {
        var (db, positions, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        var other = db.Materials.AsNoTracking().Single(m => m.MaterialNumber == 40000002);
        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 5, "Wet"), default);
        await Excess(db).Handle(new RecordExcessMaterialCommand(transactionId, other.Id, "OCT|2026", positions.Last().Id, 7, "x"), default);

        await new RejectInwardHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new RejectInwardCommand(transactionId, "Wrong GRN"), default);

        Assert.Equal(0, Stock(db, LocationSubtypes.Good));
        Assert.Equal(0, Stock(db, LocationSubtypes.Damage));
    }

    [Fact]
    public async Task DamageStock_IsNeverPickedByAPullout()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 5, "Wet"), default);

        var result = await new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(40000001, 30)], WarehouseId: 1), default);

        Assert.Equal(25, result.Lines.Single().PickedQuantityBoxes); // only the good 25 boxes
        Assert.Equal(5, Stock(db, LocationSubtypes.Damage));
    }

    [Fact]
    public async Task APartlyPickReservedPutaway_IsStillOpenForRecords_BecauseOnlyAConfirmationClosesTheGrn()
    {
        var (db, _, material) = await SeedAsync();
        using var _ = db;
        // A pullout takes 10 of the 30: the put-away splits into a closed PICK-RESERVED row (10) and an open row (20).
        await new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(material.MaterialNumber, 10)], WarehouseId: 1), default);
        var open = db.InwardPutaways.AsNoTracking().Single(p => p.ExcludedLocation == null);
        Assert.True(db.InwardPutaways.AsNoTracking().Single(p => p.ExcludedLocation == "PICK-RESERVED").IsExcludedLocked);

        var recorded = await Record(db).Handle(new RecordInwardVarianceCommand(open.Id, InwardVarianceType.Shortage, 2, "Short"), default);
        await new RemoveInwardVarianceHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new RemoveInwardVarianceCommand(recorded.Id), default);

        Assert.Equal(20, db.InwardPutaways.AsNoTracking().Single(p => p.ExcludedLocation == null).QuantityBoxes);
    }

    [Fact]
    public async Task RecordingIsNotASubmit_ThePutawayStaysOnThePickersList_AndRefreshKeepsItsCell()
    {
        var (db, _, _) = await SeedAsync();
        using var _ = db;
        var putaway = db.InwardPutaways.AsNoTracking().Single();
        await Record(db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 5, "Wet"), default);

        var row = db.InwardPutaways.AsNoTracking().Single();
        Assert.False(row.IsPickerPlaced); // still to be placed
        var pickerList = await new GetPickerPendingInwardHandler(db, new AllowAllPermissions()).Handle(new GetPickerPendingInwardQuery(), default);
        Assert.Single(pickerList.Items);

        // Refresh Put-away leaves the row (and so its Damage stock) where it is.
        var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RefreshPostedInwardPutawayHandler(
                db, new PalletAllocationService(db, new SvcClock()), TestServices.Posting(db), TestServices.Virtuals(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new RefreshPostedInwardPutawayCommand(transactionId), default));
        Assert.Equal(row.PalletPositionId, db.InwardPutaways.AsNoTracking().Single().PalletPositionId);
    }
}

file static class LocationSubtypes
{
    public const int Good = LocationSubtypeIds.Good;
    public const int Damage = LocationSubtypeIds.Damage;
}

file sealed class AllowAllPermissions : GodrejWMS.Application.Common.Permissions.IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
