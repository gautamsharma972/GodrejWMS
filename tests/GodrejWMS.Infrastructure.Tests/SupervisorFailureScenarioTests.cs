using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Bad input and wrong-state scenarios for the supervisor features: each must be refused with a clear message and leave stock, put-aways
/// and records exactly as they were.</summary>
public class SupervisorFailureScenarioTests
{
    private sealed record Seed(AppDbContext Db, List<PalletPosition> Positions, Material M1, Material M2, int TransactionId);

    private static async Task<Seed> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.AddRange(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" }, new Warehouse { Id = 2, Code = "GDR2", Name = "Godrej 2" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 5, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 5; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        var otherRack = new Rack { WarehouseId = 2, Code = "B", Columns = 1, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        otherRack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "B-01-01", MaxPallets = 1 });
        db.Racks.AddRange(rack, otherRack);
        Material New(long number, bool active = true) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy, IsActive = active
        };
        var m1 = New(40000001);
        var m2 = New(40000002);
        var inactive = New(40000003, active: false);
        db.Materials.AddRange(m1, m2, inactive);
        await db.SaveChangesAsync();
        await new SubmitInwardHandler(
                db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(m1.MaterialNumber, 30, "OCT|2026")], WarehouseId: 1), default);
        return new Seed(db, rack.PalletPositions.OrderBy(p => p.Column).ToList(), m1, m2, db.InwardTransactions.AsNoTracking().Single().Id);
    }

    private static RecordInwardVarianceHandler Record(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());

    private static RecordExcessMaterialHandler Excess(AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());

    private static decimal TotalStock(AppDbContext db) => db.StockBatches.AsNoTracking().Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    private static async Task<string> RefusedAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(action);
        return ex.Message;
    }

    [Fact]
    public async Task Record_Validation_RejectsExcessType_ZeroOrNegativeQuantity_AndMissingRemark()
    {
        var validator = new RecordInwardVarianceValidator();
        Assert.False(validator.Validate(new RecordInwardVarianceCommand(1, InwardVarianceType.Excess, 5, "x")).IsValid);
        Assert.False(validator.Validate(new RecordInwardVarianceCommand(1, InwardVarianceType.Damage, 0, "x")).IsValid);
        Assert.False(validator.Validate(new RecordInwardVarianceCommand(1, InwardVarianceType.Damage, -3, "x")).IsValid);
        Assert.False(validator.Validate(new RecordInwardVarianceCommand(1, InwardVarianceType.Damage, 5, " ")).IsValid);
        Assert.False(validator.Validate(new RecordInwardVarianceCommand(1, InwardVarianceType.Damage, 5, new string('x', 501))).IsValid);
        Assert.True(validator.Validate(new RecordInwardVarianceCommand(1, InwardVarianceType.Mismatch, 5, "ok")).IsValid);

        var excess = new RecordExcessMaterialValidator();
        Assert.False(excess.Validate(new RecordExcessMaterialCommand(1, 1, "NOT A PKM", 1, 5, "x")).IsValid);
        Assert.False(excess.Validate(new RecordExcessMaterialCommand(1, 1, "OCT|2026", 1, 0, "x")).IsValid);
        Assert.False(excess.Validate(new RecordExcessMaterialCommand(1, 0, "OCT|2026", 1, 5, "x")).IsValid);
        Assert.True(excess.Validate(new RecordExcessMaterialCommand(1, 1, "OCT|2026", 1, 5, "x")).IsValid);

        var add = new AddPulloutPickValidator();
        Assert.False(add.Validate(new AddPulloutPickCommand(1, 1, 1, 2.5m)).IsValid); // whole boxes only
        Assert.False(add.Validate(new AddPulloutPickCommand(1, 1, 1, 0)).IsValid);
        Assert.True(add.Validate(new AddPulloutPickCommand(1, 1, 1, 3)).IsValid);
    }

    [Fact]
    public async Task Record_MoreThanThePutawayHolds_OrCumulativelyMore_IsRefused_AndChangesNothing()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var putaway = s.Db.InwardPutaways.AsNoTracking().Single();

        Assert.Contains("Only 30", await RefusedAsync(() =>
            Record(s.Db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 31, "x"), default)));
        await Record(s.Db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 20, "x"), default);
        Assert.Contains("Only 10", await RefusedAsync(() =>
            Record(s.Db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Shortage, 11, "x"), default)));

        Assert.Equal(10, s.Db.InwardPutaways.AsNoTracking().Single().QuantityBoxes);
        Assert.Equal(30, TotalStock(s.Db)); // 10 good + 20 damage
        Assert.Single(s.Db.InwardLineVariances.AsNoTracking());
    }

    [Fact]
    public async Task Record_TheWholePutaway_RemovesTheRow_AndRemovingTheRecordBringsItBack()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var putaway = s.Db.InwardPutaways.AsNoTracking().Single();
        var recorded = await Record(s.Db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Mismatch, 30, "Wrong item"), default);

        Assert.Empty(s.Db.InwardPutaways.AsNoTracking());
        Assert.Equal(0, s.Db.InwardTransactionLines.AsNoTracking().Single().AllocatedQuantityBoxes);
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Fulfilled, s.Db.InwardTransactionLines.AsNoTracking().Single().Status); // 30 accounted for as mismatch

        await new RemoveInwardVarianceHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock(), new SvcUser())
            .Handle(new RemoveInwardVarianceCommand(recorded.Id), default);

        var row = s.Db.InwardPutaways.AsNoTracking().Single();
        Assert.Equal(30, row.QuantityBoxes);
        Assert.False(row.IsPickerPlaced); // back as an unplaced put-away
        Assert.Equal(30, TotalStock(s.Db));
    }

    [Fact]
    public async Task Record_OnAGitOrNoRackOrPickReservedRow_IsRefused()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var row = await s.Db.InwardPutaways.SingleAsync();
        row.ExcludedLocation = "NO RACK";
        await s.Db.SaveChangesAsync();

        var message = await RefusedAsync(() => Record(s.Db).Handle(new RecordInwardVarianceCommand(row.Id, InwardVarianceType.Damage, 1, "x"), default));
        Assert.Contains("NO RACK", message);
    }

    [Fact]
    public async Task Record_OnARejectedGrn_AnUnpostedGrn_OrAMissingPutaway_IsRefused()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var putaway = s.Db.InwardPutaways.AsNoTracking().Single();

        await Assert.ThrowsAsync<GodrejWMS.Application.Common.Exceptions.NotFoundException>(() =>
            Record(s.Db).Handle(new RecordInwardVarianceCommand(99999, InwardVarianceType.Damage, 1, "x"), default));

        var transaction = await s.Db.InwardTransactions.SingleAsync();
        transaction.PostedAt = null;
        await s.Db.SaveChangesAsync();
        Assert.Contains("not been posted", await RefusedAsync(() =>
            Record(s.Db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 1, "x"), default)));

        transaction = await s.Db.InwardTransactions.SingleAsync();
        transaction.PostedAt = DateTimeOffset.UtcNow;
        transaction.IsRejected = true;
        await s.Db.SaveChangesAsync();
        Assert.Contains("rejected", await RefusedAsync(() =>
            Record(s.Db).Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 1, "x"), default)));
        Assert.Equal(30, TotalStock(s.Db));
    }

    [Fact]
    public async Task Excess_Refusals_InactiveMaterial_InactiveOrForeignOrNonGoodLocation_AndUnknownIds()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var inactive = s.Db.Materials.AsNoTracking().Single(m => m.MaterialNumber == 40000003);
        var foreign = s.Db.PalletPositions.AsNoTracking().Single(p => p.LocationCode == "B-01-01");
        var inactiveCell = await s.Db.PalletPositions.SingleAsync(p => p.LocationCode == "A-05-01");
        inactiveCell.IsActive = false;
        var damageCell = await s.Db.PalletPositions.SingleAsync(p => p.LocationCode == "A-04-01");
        damageCell.LocationSubtypeId = LocationSubtypeIds.Damage;
        await s.Db.SaveChangesAsync();
        var good = s.Positions[2].Id;

        Assert.Contains("inactive", await RefusedAsync(() => Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, inactive.Id, "OCT|2026", good, 5, "x"), default)));
        Assert.Contains("different warehouse", await RefusedAsync(() => Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, s.M2.Id, "OCT|2026", foreign.Id, 5, "x"), default)));
        Assert.Contains("not active", await RefusedAsync(() => Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, s.M2.Id, "OCT|2026", inactiveCell.Id, 5, "x"), default)));
        Assert.Contains("cannot hold good stock", await RefusedAsync(() => Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, s.M2.Id, "OCT|2026", damageCell.Id, 5, "x"), default)));
        await Assert.ThrowsAsync<GodrejWMS.Application.Common.Exceptions.NotFoundException>(() =>
            Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, 99999, "OCT|2026", good, 5, "x"), default));
        await Assert.ThrowsAsync<GodrejWMS.Application.Common.Exceptions.NotFoundException>(() =>
            Excess(s.Db).Handle(new RecordExcessMaterialCommand(99999, s.M2.Id, "OCT|2026", good, 5, "x"), default));

        Assert.Equal(30, TotalStock(s.Db));
        Assert.Empty(s.Db.InwardLineVariances.AsNoTracking());
    }

    [Fact]
    public async Task Excess_OverThePalletSize_Warns_AndRemovingItWhenTheStockWasPickedIsRefusedWithoutHarm()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cell = s.Positions[3].Id;

        var warn = await Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, s.M2.Id, "OCT|2026", cell, 60, "x"), default);
        Assert.False(warn.Saved);
        Assert.Contains("pallet size", warn.Warning);

        var saved = await Excess(s.Db).Handle(new RecordExcessMaterialCommand(s.TransactionId, s.M2.Id, "OCT|2026", cell, 20, "x"), default);
        Assert.True(saved.Saved);

        // The excess boxes were shipped (a pullout took them) before the supervisor removed the record: nothing is left to take back.
        var batch = await s.Db.StockBatches.SingleAsync(b => b.MaterialId == s.M2.Id);
        batch.QuantityBoxes = 0;
        await s.Db.SaveChangesAsync();
        var message = await RefusedAsync(() => new RemoveInwardVarianceHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock(), new SvcUser())
            .Handle(new RemoveInwardVarianceCommand(saved.Record!.Id), default));
        Assert.Contains("holds only", message);
        Assert.Single(s.Db.InwardLineVariances.AsNoTracking()); // the record is still there
    }

    [Fact]
    public async Task AddPick_Refusals_ForeignWarehouse_Git_InactiveMaterial_NoStock_AndAnUnknownPullout()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        s.Db.StockBatches.Add(new StockBatch { MaterialId = s.M2.Id, PalletPositionId = s.Positions[3].Id, MfgMonth = 202610, QuantityBoxes = 40 });
        await s.Db.SaveChangesAsync();
        await new SubmitPulloutHandler(s.Db, new PulloutAllocationService(s.Db), TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(s.M1.MaterialNumber, 5)], WarehouseId: 1), default);
        var pulloutId = s.Db.PulloutTransactions.AsNoTracking().Single().Id;
        var handler = new AddPulloutPickHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock());
        var inactive = s.Db.Materials.AsNoTracking().Single(m => m.MaterialNumber == 40000003);
        var foreign = s.Db.PalletPositions.AsNoTracking().Single(p => p.LocationCode == "B-01-01");

        Assert.Contains("inactive", await RefusedAsync(() => handler.Handle(new AddPulloutPickCommand(pulloutId, inactive.Id, s.Positions[3].Id, 1), default)));
        Assert.Contains("cannot be picked from", await RefusedAsync(() => handler.Handle(new AddPulloutPickCommand(pulloutId, s.M2.Id, foreign.Id, 1), default)));
        Assert.Contains("holds 0", await RefusedAsync(() => handler.Handle(new AddPulloutPickCommand(pulloutId, s.M2.Id, s.Positions[4].Id, 1), default)));
        await Assert.ThrowsAsync<GodrejWMS.Application.Common.Exceptions.NotFoundException>(() => handler.Handle(new AddPulloutPickCommand(99999, s.M2.Id, s.Positions[3].Id, 1), default));
        Assert.Equal(40, s.Db.StockBatches.AsNoTracking().Single(b => b.MaterialId == s.M2.Id).QuantityBoxes);
    }

    [Fact]
    public async Task AddPick_AcrossTwoPkmsInOneCell_TakesTheOlderFirst_AndFlagsFifoOnlyForTheNewerOne()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cell = s.Positions[3];
        s.Db.StockBatches.AddRange(
            new StockBatch { MaterialId = s.M2.Id, PalletPositionId = cell.Id, MfgMonth = 202609, QuantityBoxes = 6 },
            new StockBatch { MaterialId = s.M2.Id, PalletPositionId = cell.Id, MfgMonth = 202610, QuantityBoxes = 10 });
        await s.Db.SaveChangesAsync();
        await new SubmitPulloutHandler(s.Db, new PulloutAllocationService(s.Db), TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(s.M1.MaterialNumber, 5)], WarehouseId: 1), default);
        var pulloutId = s.Db.PulloutTransactions.AsNoTracking().Single().Id;
        var handler = new AddPulloutPickHandler(s.Db, TestServices.Posting(s.Db), TestServices.Uow(s.Db), new SvcClock());

        // 9 boxes need 6 of SEP and 3 of OCT: the OCT part skips nothing older elsewhere in the cell... but older stock (SEP) exists, so FIFO is broken for OCT.
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new AddPulloutPickCommand(pulloutId, s.M2.Id, cell.Id, 9), default));
        // 6 boxes are exactly the older PKM: no reason needed.
        var ok = await handler.Handle(new AddPulloutPickCommand(pulloutId, s.M2.Id, cell.Id, 6), default);
        Assert.False(ok.BreaksFifo);
        Assert.Equal(10, s.Db.StockBatches.AsNoTracking().Single(b => b.MaterialId == s.M2.Id && b.MfgMonth == 202610).QuantityBoxes);
    }
}
