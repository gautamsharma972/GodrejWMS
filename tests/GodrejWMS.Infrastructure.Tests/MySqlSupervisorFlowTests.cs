using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>The supervisor features against a REAL MySQL (set GODREJWMS_TEST_MYSQL, see <see cref="MySqlFactAttribute"/>): the EF in-memory provider does not
/// translate queries to SQL and enforces no unique indexes or foreign keys, so this is where the new GROUP BY / subquery queries, the
/// (material, PKM, cell, stock subtype) unique index and the FK constraints are really exercised. After every step the Stock Reconciliation report
/// must stay free of problems - ledger, stock and put-aways agree.</summary>
public class MySqlSupervisorFlowTests
{
    private static string Server => Environment.GetEnvironmentVariable(MySqlFactAttribute.Variable)!.TrimEnd(';') + ";";

    private static AppDbContext Open(string database) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql($"{Server}Database={database};", new MySqlServerVersion(new Version(8, 0, 36)))
                .Options,
            new IntUser(), new SvcClock());

    private sealed class IntUser : ICurrentUserService
    {
        public string? UserId => "it-user";
        public string? UserName => "it@godrejwms.local";
        public bool IsInRole(string role) => true;
        public IReadOnlyList<string> Roles => ["Superadmin"];
    }

    private sealed class AllowAll : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class OnlyPicker : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(permissionKey is PermissionKeys.Inward.AssignPkm or PermissionKeys.Pullout.Pick);
    }

    private sealed class OneWarehouse : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([new WarehouseOption(1, "GDR1", "Godrej 1")]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static async Task SeedReferenceAsync(AppDbContext db)
    {
        db.Seasons.Add(new Season { Id = SeasonIds.Rainy, Code = "RAINY", DisplayName = "Rainy" });
        db.SkuMovementTypes.AddRange(
            new SkuMovementType { Id = SkuMovementTypeIds.FastMoving, Code = "FAST", DisplayName = "Fast" },
            new SkuMovementType { Id = SkuMovementTypeIds.SlowMoving, Code = "SLOW", DisplayName = "Slow" });
        db.LocationTypes.AddRange(
            new LocationType { Id = LocationTypeIds.Rack, Code = "RACK", DisplayName = "Rack" },
            new LocationType { Id = LocationTypeIds.Floor, Code = "FLOOR", DisplayName = "Floor" });
        db.LocationSubtypes.AddRange(
            new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" },
            new LocationSubtype { Id = LocationSubtypeIds.Damage, Code = "DAMAGE", DisplayName = "Damage" });
        db.ZoneTypes.AddRange(
            new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" },
            new ZoneType { Id = ZoneTypeIds.Overflow, Code = "Overflow", DisplayName = "Overflow" },
            new ZoneType { Id = ZoneTypeIds.Git, Code = "GIT", DisplayName = "GIT" });
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        await db.SaveChangesAsync();
    }

    private static async Task AssertReconciledAsync(AppDbContext db, string step)
    {
        var report = await new GetStockReconciliationReportHandler(db, new AllowAll(), new OneWarehouse())
            .Handle(new GetStockReconciliationReportQuery(), default);
        var problems = report.Issues.Where(i => i.Severity != "Info").Select(i => $"{i.Check}: {i.Location} {i.Reference} expected {i.Expected} actual {i.Actual}").ToList();
        Assert.True(problems.Count == 0, $"Reconciliation found problems after '{step}': {string.Join(" | ", problems)}");
    }

    [MySqlFact]
    public async Task SupervisorFlow_RunsEndToEnd_OnMySql_AndStaysReconciled()
    {
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        await using var db = Open(database);
        try
        {
            await db.Database.EnsureCreatedAsync();
            await SeedReferenceAsync(db);
            var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 5, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
            for (var c = 1; c <= 5; c++)
            {
                rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
            }

            db.Racks.Add(rack);
            Material NewMaterial(long number) => new()
            {
                MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
                LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
            };
            var m1 = NewMaterial(40000001);
            var m2 = NewMaterial(40000002);
            db.Materials.AddRange(m1, m2);
            await db.SaveChangesAsync();
            var positions = rack.PalletPositions.OrderBy(p => p.Column).ToList();

            // 1. Inward upload (30 boxes, posted on upload).
            await new SubmitInwardHandler(
                    db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new SubmitInwardCommand([new SubmitInwardLine(m1.MaterialNumber, 30, "OCT|2026")], WarehouseId: 1), default);
            var transactionId = db.InwardTransactions.AsNoTracking().Single().Id;
            await AssertReconciledAsync(db, "inward upload");

            // 2. Damage + shortage recorded against the put-away: a Good and a Damage batch share one cell (the new unique index).
            var putaway = db.InwardPutaways.AsNoTracking().Single();
            var record = new RecordInwardVarianceHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());
            var damage = await record.Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Damage, 5, "Wet"), default);
            await record.Handle(new RecordInwardVarianceCommand(putaway.Id, InwardVarianceType.Shortage, 3, "Short"), default);
            Assert.Equal(22, db.StockBatches.AsNoTracking().Where(b => b.StockSubtypeId == LocationSubtypeIds.Good).Sum(b => b.QuantityBoxes));
            Assert.Equal(5, db.StockBatches.AsNoTracking().Where(b => b.StockSubtypeId == LocationSubtypeIds.Damage).Sum(b => b.QuantityBoxes));
            await AssertReconciledAsync(db, "damage + shortage");

            // 3. Excess material of another material at another location.
            var excess = await new RecordExcessMaterialHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new RecordExcessMaterialCommand(transactionId, m2.Id, "OCT|2026", positions.Last().Id, 12, "Found on the vehicle"), default);
            Assert.True(excess.Saved);
            await AssertReconciledAsync(db, "excess material");

            // 4. The queries behind the screens translate to SQL and agree.
            var detail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(transactionId), default);
            Assert.Equal(3, detail.Variances!.Count);
            Assert.Equal(8, detail.Lines.Single().VarianceBoxes);
            Assert.Equal(0, detail.Lines.Single().UnplacedBoxes);
            Assert.Equal(0, detail.Lines.Single().LeftInventoryBoxes);
            var pending = await new GetPendingConfirmationReportHandler(db, new AllowAll(), new SvcClock()).Handle(new GetPendingConfirmationReportQuery(), default);
            Assert.Single(pending.Rows);
            var picker = await new GetPickerPendingInwardHandler(db, new OnlyPicker()).Handle(new GetPickerPendingInwardQuery(), default);
            var supervisor = await new GetPickerPendingInwardHandler(db, new AllowAll()).Handle(new GetPickerPendingInwardQuery(), default);
            Assert.Single(picker.Items);
            Assert.Single(supervisor.Items);
            Assert.Equal(1, (await new GetPickerPendingCountsHandler(db, new OnlyPicker()).Handle(new GetPickerPendingCountsQuery(), default)).PutawayGrns);

            // 5. Refresh Put-away must not re-allocate what the records account for.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new RefreshPostedInwardPutawayHandler(
                        db, new PalletAllocationService(db, new SvcClock()), TestServices.Posting(db), TestServices.Virtuals(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                    .Handle(new RefreshPostedInwardPutawayCommand(transactionId), default));
            Assert.Equal(22, db.StockBatches.AsNoTracking().Where(b => b.StockSubtypeId == LocationSubtypeIds.Good && b.MaterialId == m1.Id).Sum(b => b.QuantityBoxes));

            // 6. A pullout takes part of the put-away (PICK-RESERVED), the supervisor adds the excess material and a zero pick skips one.
            await new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(m1.MaterialNumber, 10)], WarehouseId: 1), default);
            var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;
            await AssertReconciledAsync(db, "pullout upload");
            var options = await new GetPulloutAddOptionsHandler(db).Handle(new GetPulloutAddOptionsQuery(pulloutId, m2.Id), default);
            Assert.Single(options);
            await new AddPulloutPickHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock())
                .Handle(new AddPulloutPickCommand(pulloutId, m2.Id, options[0].PalletPositionId, 4), default);
            await AssertReconciledAsync(db, "supervisor added a pick");
            var pulloutDetail = await new GetPulloutDetailHandler(db).Handle(new GetPulloutDetailQuery(pulloutId), default);
            Assert.Equal(2, pulloutDetail.Lines.Count);

            // 7. Removing the records restores the stock; rejecting the pullout and the GRN leaves a clean ledger.
            var remove = new RemoveInwardVarianceHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser());
            await remove.Handle(new RemoveInwardVarianceCommand(damage.Id), default);
            await AssertReconciledAsync(db, "damage record removed");

            await new RejectPulloutHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new RejectPulloutCommand(db.PulloutTransactions.AsNoTracking().Single().ReferenceNumber, "test"), default);
            await new RejectInwardHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new RejectInwardCommand(transactionId, "test"), default);
            Assert.Equal(0, db.StockBatches.AsNoTracking().Where(b => b.MaterialId == m1.Id).Sum(b => b.QuantityBoxes) + db.StockBatches.AsNoTracking().Where(b => b.MaterialId == m2.Id).Sum(b => b.QuantityBoxes) - await ExpectedLeftoverAsync(db, m2.Id, m1.Id));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    // After rejecting both orders, only boxes that cannot be reversed may remain (e.g. none here): everything else must be zero.
    private static Task<decimal> ExpectedLeftoverAsync(AppDbContext db, int m2, int m1) => Task.FromResult(0m);

    [MySqlFact]
    public async Task UniqueIndex_AllowsGoodAndDamageInOneCell_ButNotTwoBatchesOfTheSameSubtype()
    {
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        await using var db = Open(database);
        try
        {
            await db.Database.EnsureCreatedAsync();
            await SeedReferenceAsync(db);
            var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 });
            db.Racks.Add(rack);
            var material = new Material
            {
                MaterialNumber = 40000001, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
                LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
            };
            db.Materials.Add(material);
            await db.SaveChangesAsync();
            var position = rack.PalletPositions.Single();

            db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202610, StockSubtypeId = LocationSubtypeIds.Good, QuantityBoxes = 10 });
            db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202610, StockSubtypeId = LocationSubtypeIds.Damage, QuantityBoxes = 2 });
            await db.SaveChangesAsync(); // allowed: different subtype

            db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202610, StockSubtypeId = LocationSubtypeIds.Damage, QuantityBoxes = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); // duplicate of the same subtype
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(AppDbContext Db, List<PalletPosition> Positions, Material M1, Material M2)> SetupAsync(string database, int columns = 8)
    {
        var db = Open(database);
        await db.Database.EnsureCreatedAsync();
        await SeedReferenceAsync(db);
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = columns, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= columns; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        Material NewMaterial(long number) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        var m1 = NewMaterial(40000001);
        var m2 = NewMaterial(40000002);
        db.Materials.AddRange(m1, m2);
        await db.SaveChangesAsync();
        return (db, rack.PalletPositions.OrderBy(p => p.Column).ToList(), m1, m2);
    }

    private static Task UploadInwardAsync(AppDbContext db, Material material, decimal quantity) =>
        new SubmitInwardHandler(db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitInwardCommand([new SubmitInwardLine(material.MaterialNumber, quantity, "OCT|2026")], WarehouseId: 1), default);

    private static Task UploadPulloutAsync(AppDbContext db, Material material, decimal quantity) =>
        new SubmitPulloutHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
            .Handle(new SubmitPulloutCommand([new SubmitPulloutLine(material.MaterialNumber, quantity)], WarehouseId: 1), default);

    [MySqlFact]
    public async Task PickReservedZeroPickAndRefreshPick_RunOnMySql_AndStayReconciled()
    {
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        var (db, positions, m1, _) = await SetupAsync(database);
        await using var _ = db;
        try
        {
            // GRN 1 (30) is posted; a pullout takes all of it before the picker placed it -> PICK-RESERVED.
            await UploadInwardAsync(db, m1, 30);
            await UploadPulloutAsync(db, m1, 30);
            Assert.Equal("PICK-RESERVED", db.InwardPutaways.AsNoTracking().Single().ExcludedLocation);
            await AssertReconciledAsync(db, "pick-reserved");

            // The material is still on the vehicle: the picker submits 0, the supervisor confirms the pick-list.
            var pick = db.PulloutPicks.AsNoTracking().Single();
            await new ConfirmPulloutPickHandler(db, new AllowAll(), new SvcClock(), new SvcUser()).Handle(new ConfirmPulloutPickCommand(pick.Id, 0), default);
            var pulloutId = db.PulloutTransactions.AsNoTracking().Single().Id;
            await new ConfirmPulloutPicklistHandler(db, TestServices.Posting(db), TestServices.Uow(db), new AllowAll(), new SvcClock(), new SvcUser())
                .Handle(new ConfirmPulloutPicklistCommand(pulloutId), default);
            Assert.Null(db.InwardPutaways.AsNoTracking().Single().ExcludedLocation); // the put-away is open again
            Assert.Equal(30, db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
            await AssertReconciledAsync(db, "zero pick confirmed");

            // GRN 2 (30) + a pullout of 40 spanning both GRNs' cells; then GRN 1 is rejected and Refresh Pick re-allocates the invalid picks.
            await UploadInwardAsync(db, m1, 30);
            await UploadPulloutAsync(db, m1, 40);
            var second = db.PulloutTransactions.AsNoTracking().OrderBy(t => t.Id).Last();
            await AssertReconciledAsync(db, "second pullout");
            var grn1 = db.InwardTransactions.AsNoTracking().OrderBy(t => t.Id).First();
            await new RejectInwardHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new RejectInwardCommand(grn1.Id, "Wrong GRN"), default);

            var detail = await new GetPulloutDetailHandler(db).Handle(new GetPulloutDetailQuery(second.Id), default);
            Assert.True(detail.CanRefreshPick);
            var refreshed = await new RefreshPulloutPicksHandler(db, new PulloutAllocationService(db), TestServices.Posting(db), TestServices.Uow(db), new SvcClock())
                .Handle(new RefreshPulloutPicksCommand(second.ReferenceNumber), default);
            Assert.True(refreshed.DroppedBoxes > 0);
            await AssertReconciledAsync(db, "refresh pick");

            // The rejected GRN and the pending report translate to SQL too.
            var rejectedDetail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(grn1.Id), default);
            Assert.True(rejectedDetail.IsRejected);
            await new GetPendingConfirmationReportHandler(db, new AllowAll(), new SvcClock()).Handle(new GetPendingConfirmationReportQuery(), default);

            // Rejecting the second pullout leaves pending reservations that Refresh Put-away turns back into stock, exactly once.
            await new RejectPulloutHandler(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new RejectPulloutCommand(second.ReferenceNumber, "test"), default);
            var grn2 = db.InwardTransactions.AsNoTracking().OrderBy(t => t.Id).Last();
            var before = db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes);
            try
            {
                await new RefreshPostedInwardPutawayHandler(
                        db, new PalletAllocationService(db, new SvcClock()), TestServices.Posting(db), TestServices.Virtuals(db), TestServices.Uow(db), new SvcClock(), new SvcUser())
                    .Handle(new RefreshPostedInwardPutawayCommand(grn2.Id), default);
            }
            catch (InvalidOperationException)
            {
                // "Nothing to refresh" is acceptable when the rejection left nothing pending.
            }

            Assert.True(db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes) >= before);
            await AssertReconciledAsync(db, "refresh put-away after pullout reject");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [MySqlFact]
    public async Task TwoSupervisorsRecordingAtTheSameTime_NeverTakeMoreThanThePutawayHolds()
    {
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        var (db, _, m1, _) = await SetupAsync(database);
        await using var _ = db;
        try
        {
            await UploadInwardAsync(db, m1, 30);
            var putawayId = db.InwardPutaways.AsNoTracking().Single().Id;

            using var barrier = new Barrier(2);
            async Task<string> TryRecordAsync()
            {
                await using var other = Open(database);
                var handler = new RecordInwardVarianceHandler(other, TestServices.Posting(other), TestServices.Uow(other), new SvcClock(), new SvcUser());
                barrier.SignalAndWait();
                try
                {
                    await handler.Handle(new RecordInwardVarianceCommand(putawayId, InwardVarianceType.Damage, 20, "Wet"), default);
                    return "saved";
                }
                catch (InvalidOperationException ex)
                {
                    return ex.Message; // a friendly refusal, never a raw database error
                }
            }

            var results = await Task.WhenAll(Task.Run(TryRecordAsync), Task.Run(TryRecordAsync));

            Assert.Equal(1, results.Count(r => r == "saved"));
            Assert.Equal(10, db.InwardPutaways.AsNoTracking().Single().QuantityBoxes);
            Assert.Equal(10, db.StockBatches.AsNoTracking().Where(b => b.StockSubtypeId == LocationSubtypeIds.Good).Sum(b => b.QuantityBoxes));
            Assert.Equal(20, db.StockBatches.AsNoTracking().Where(b => b.StockSubtypeId == LocationSubtypeIds.Damage).Sum(b => b.QuantityBoxes));
            Assert.Single(db.InwardLineVariances.AsNoTracking());
            await AssertReconciledAsync(db, "concurrent records");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [MySqlFact]
    public async Task InwardWithoutPkm_AssigningAPkm_FromAFreshRequestContext_KeepsEveryRow_OnMySql()
    {
        // Like the mobile API: every request gets its own context, so nothing from the upload is tracked when the picker submits.
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        var (db, _, m1, _) = await SetupAsync(database);
        await using var _ = db;
        try
        {
            await new SubmitInwardWithoutPkmHandler(
                    db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(m1.MaterialNumber, 120)], WarehouseId: 1), default);
            var rows = db.InwardPutaways.AsNoTracking().OrderBy(p => p.Id).ToList();

            await using (var request = Open(database))
            {
                var place = new PickerPlaceInwardPutawayHandler(request, TestServices.Posting(request), TestServices.Git(request), TestServices.Uow(request), new AllowAll(), new SvcUser(), new SvcClock());
                await place.Handle(new PickerPlaceInwardPutawayCommand(rows[0].Id, 50, "NOV|2026", rows[0].PalletPositionId, null, AcknowledgeWarning: true), default);
            }

            // The location that got its PKM is still there (before the fix it was deleted, taking 50 boxes out of the put-away list).
            await using var check = Open(database);
            var after = check.InwardPutaways.AsNoTracking().OrderBy(p => p.Id).ToList();
            Assert.Equal(3, after.Count);
            Assert.Equal(120, after.Sum(p => p.QuantityBoxes));
            Assert.Equal(202611, check.InwardTransactionLines.AsNoTracking().Single(l => l.Id == after[0].InwardTransactionLineId).MfgMonth);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [MySqlFact]
    public async Task InwardWithoutPkm_AssigningAPkmToOneOfSeveralRows_KeepsEveryRow_OnMySql()
    {
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        var (db, _, m1, _) = await SetupAsync(database);
        await using var _ = db;
        try
        {
            await new SubmitInwardWithoutPkmHandler(
                    db, TestServices.Placement(db, new PalletAllocationService(db, new SvcClock())), TestServices.Uow(db), new SvcClock(), new SvcUser())
                .Handle(new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(m1.MaterialNumber, 120)], WarehouseId: 1), default);
            var rows = db.InwardPutaways.AsNoTracking().OrderBy(p => p.Id).ToList();
            Assert.Equal(3, rows.Count);

            var place = new PickerPlaceInwardPutawayHandler(db, TestServices.Posting(db), TestServices.Git(db), TestServices.Uow(db), new AllowAll(), new SvcUser(), new SvcClock());
            await place.Handle(new PickerPlaceInwardPutawayCommand(rows[0].Id, 50, "NOV|2026", rows[0].PalletPositionId, null, AcknowledgeWarning: true), default);

            Assert.Equal(3, db.InwardPutaways.AsNoTracking().Count()); // the location that got its PKM must still be there
            Assert.Equal(120, db.InwardPutaways.AsNoTracking().Sum(p => p.QuantityBoxes));
            var tooMany = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                place.Handle(new PickerPlaceInwardPutawayCommand(rows[1].Id, 80, "NOV|2026", rows[1].PalletPositionId, null, AcknowledgeWarning: true), default));
            Assert.Contains("more than the 120", tooMany.Message);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
