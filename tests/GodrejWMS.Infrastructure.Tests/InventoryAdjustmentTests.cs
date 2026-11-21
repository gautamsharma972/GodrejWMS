using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.InventoryAdjustments.Commands;
using GodrejWMS.Application.Features.InventoryAdjustments.Queries;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class InventoryAdjustmentTests
{
    [Fact]
    public async Task SupervisorSubmission_AutoApprovesAndUpdatesLiveInventory()
    {
        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var db = CreateDb(Guid.NewGuid().ToString(), supervisor);
        var batchId = await SeedAsync(db, 20);

        var result = await new SubmitInventoryAdjustmentHandler(
                db, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new SubmitInventoryAdjustmentCommand(
                batchId, 16, InventoryAdjustmentReasonCodes.PhysicalCountVariance, "Supervisor verified count"),
                CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Approved, result.Status);
        Assert.Equal("supervisor@test", result.ReviewedByUserName);
        Assert.Equal(16, db.StockBatches.Single(b => b.Id == batchId).QuantityBoxes);
        Assert.Equal(20, result.ActualQuantityBeforePosting);
        Assert.Equal(16, result.ActualQuantityAfterPosting);
    }

    [Fact]
    public async Task ApprovedAdjustment_WritesAnAdjustmentLedgerEntryForTheChange()
    {
        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var db = CreateDb(Guid.NewGuid().ToString(), supervisor);
        var batchId = await SeedAsync(db, 20);

        await new SubmitInventoryAdjustmentHandler(
                db, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new SubmitInventoryAdjustmentCommand(
                batchId, 0, InventoryAdjustmentReasonCodes.PhysicalCountVariance, "Boxes are not on the shelf"),
                CancellationToken.None);

        var entry = Assert.Single(db.StockLedgerEntries.AsNoTracking());
        Assert.Equal(StockLedgerEntryTypes.Adjustment, entry.EntryType);
        Assert.Equal(-20, entry.QuantityBoxes);
        Assert.Equal(0, entry.BalanceAfterBoxes);
        Assert.StartsWith("ADJ", entry.ReferenceNumber);
    }

    [Fact]
    public async Task MaterialCorrection_WaitsForApproval_ThenReplacesSystemSkuAtSameLocation()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requester = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var operatorDb = CreateDb(databaseName, requester);
        var originalBatchId = await SeedAsync(operatorDb, 40);
        var physicalMaterial = NewMaterial(40039574, "Physically found SKU");
        operatorDb.Materials.Add(physicalMaterial);
        await operatorDb.SaveChangesAsync();

        var submitted = await new SubmitInventoryAdjustmentHandler(
                operatorDb, new AdjustmentClock(), requester, new AdjustmentNotifications(), new FakeAdjustmentPermissions(requester))
            .Handle(new SubmitInventoryAdjustmentCommand(
                originalBatchId, 35, InventoryAdjustmentReasonCodes.DataEntryCorrection,
                "Wrong SKU recorded in this pallet position", physicalMaterial.Id), CancellationToken.None);

        Assert.Equal(40, operatorDb.StockBatches.Single(b => b.Id == originalBatchId).QuantityBoxes);
        Assert.False(operatorDb.StockBatches.Any(b => b.MaterialId == physicalMaterial.Id));
        Assert.Equal(physicalMaterial.MaterialNumber, submitted.PhysicalMaterialNumber);

        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var supervisorDb = CreateDb(databaseName, supervisor);
        var approved = await new ApproveInventoryAdjustmentHandler(
                supervisorDb, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id, "Physical SKU verified"), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Approved, approved.Status);
        Assert.False(supervisorDb.StockBatches.Any(b => b.Id == originalBatchId));
        var replacement = Assert.Single(supervisorDb.StockBatches.Where(b => b.MaterialId == physicalMaterial.Id));
        Assert.Equal(35, replacement.QuantityBoxes);
        Assert.Equal(202610, replacement.MfgMonth);
    }

    [Fact]
    public async Task Submit_DoesNotChangeStock_AndSupervisorApprovalPostsCount()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var operatorDb = CreateDb(databaseName, new AdjustmentUser("operator", "operator@test", "Office Staff"));
        var batchId = await SeedAsync(operatorDb, 60);
        var submitNotifications = new AdjustmentNotifications();
        var operatorUser = new AdjustmentUser("operator", "operator@test", "Office Staff");
        var submitted = await new SubmitInventoryAdjustmentHandler(operatorDb, new AdjustmentClock(), operatorUser, submitNotifications, new FakeAdjustmentPermissions(operatorUser))
            .Handle(new SubmitInventoryAdjustmentCommand(batchId, 50, InventoryAdjustmentReasonCodes.PhysicalCountVariance, "Counted twice"), CancellationToken.None);

        Assert.Equal(60, operatorDb.StockBatches.Single(b => b.Id == batchId).QuantityBoxes);
        Assert.Equal(InventoryAdjustmentStatus.PendingApproval, submitted.Status);
        var reviewNotification = Assert.Single(submitNotifications.Published, n => !n.Silent);
        Assert.Contains(submitNotifications.Published, n => n.Silent && n.Topic == RealtimeTopics.InventoryAdjustmentsChanged);
        Assert.Contains("Supervisor", reviewNotification.AudienceRoles!);
        Assert.Equal("/inventory-adjustments", reviewNotification.Url);

        await using var supervisorDb = CreateDb(databaseName, new AdjustmentUser("supervisor", "supervisor@test", "Supervisor"));
        var approvalNotifications = new AdjustmentNotifications();
        var supervisorUser = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        var approved = await new ApproveInventoryAdjustmentHandler(supervisorDb, new AdjustmentClock(), supervisorUser, approvalNotifications, new FakeAdjustmentPermissions(supervisorUser))
            .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id, "Verified"), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Approved, approved.Status);
        Assert.Equal(50, supervisorDb.StockBatches.Single(b => b.Id == batchId).QuantityBoxes);
        Assert.Equal(60, approved.ActualQuantityBeforePosting);
        Assert.Equal(50, approved.ActualQuantityAfterPosting);
        var requesterNotification = Assert.Single(approvalNotifications.Published, n => !n.Silent);
        Assert.Contains(approvalNotifications.Published, n => n.Silent && n.Topic == RealtimeTopics.InventoryAdjustmentsChanged);
        Assert.Equal("operator", requesterNotification.RecipientUserId);
        Assert.Equal("success", requesterNotification.Kind);
    }

    [Fact]
    public async Task Approval_MarksConflict_WhenStockChangedAfterCount()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var operatorDb = CreateDb(databaseName, new AdjustmentUser("operator", "operator@test", "Office Staff"));
        var batchId = await SeedAsync(operatorDb, 60);
        var operatorUser = new AdjustmentUser("operator", "operator@test", "Office Staff");
        var submitted = await new SubmitInventoryAdjustmentHandler(operatorDb, new AdjustmentClock(), operatorUser, new AdjustmentNotifications(), new FakeAdjustmentPermissions(operatorUser))
            .Handle(new SubmitInventoryAdjustmentCommand(batchId, 50, InventoryAdjustmentReasonCodes.PhysicalCountVariance, null), CancellationToken.None);

        var batch = operatorDb.StockBatches.Single(b => b.Id == batchId);
        batch.QuantityBoxes = 55;
        batch.RowVersion++;
        await operatorDb.SaveChangesAsync();

        await using var supervisorDb = CreateDb(databaseName, new AdjustmentUser("supervisor", "supervisor@test", "Supervisor"));
        var supervisorUser = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        var result = await new ApproveInventoryAdjustmentHandler(supervisorDb, new AdjustmentClock(), supervisorUser, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisorUser))
            .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Conflict, result.Status);
        Assert.Equal(55, supervisorDb.StockBatches.Single(b => b.Id == batchId).QuantityBoxes);
    }

    [Fact]
    public async Task Operator_CannotApproveAdjustment()
    {
        var databaseName = Guid.NewGuid().ToString();
        var user = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var db = CreateDb(databaseName, user);
        var batchId = await SeedAsync(db, 60);
        var submitted = await new SubmitInventoryAdjustmentHandler(db, new AdjustmentClock(), user, new AdjustmentNotifications(), new FakeAdjustmentPermissions(user))
            .Handle(new SubmitInventoryAdjustmentCommand(batchId, 50, InventoryAdjustmentReasonCodes.PhysicalCountVariance, null), CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ApproveInventoryAdjustmentHandler(db, new AdjustmentClock(), user, new AdjustmentNotifications(), new FakeAdjustmentPermissions(user))
                .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id), CancellationToken.None));
    }

    [Fact]
    public async Task DuplicatePendingRequest_ForSameBatch_IsRejected()
    {
        var user = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var db = CreateDb(Guid.NewGuid().ToString(), user);
        var batchId = await SeedAsync(db, 60);
        var handler = new SubmitInventoryAdjustmentHandler(db, new AdjustmentClock(), user, new AdjustmentNotifications(), new FakeAdjustmentPermissions(user));

        await handler.Handle(new SubmitInventoryAdjustmentCommand(batchId, 50, InventoryAdjustmentReasonCodes.PhysicalCountVariance, null), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new SubmitInventoryAdjustmentCommand(batchId, 45, InventoryAdjustmentReasonCodes.PhysicalCountVariance, null), CancellationToken.None));
        Assert.Contains("pending adjustment already exists", error.Message);
        Assert.Equal(60, db.StockBatches.Single(b => b.Id == batchId).QuantityBoxes);
    }

    [Fact]
    public async Task Operator_CannotViewAnotherOperatorsAdjustmentDetail()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requester = new AdjustmentUser("operator-1", "operator1@test", "Office Staff");
        await using var requesterDb = CreateDb(databaseName, requester);
        var batchId = await SeedAsync(requesterDb, 60);
        var submitted = await new SubmitInventoryAdjustmentHandler(requesterDb, new AdjustmentClock(), requester, new AdjustmentNotifications(), new FakeAdjustmentPermissions(requester))
            .Handle(new SubmitInventoryAdjustmentCommand(batchId, 50, InventoryAdjustmentReasonCodes.PhysicalCountVariance, null), CancellationToken.None);

        var otherOperator = new AdjustmentUser("operator-2", "operator2@test", "Office Staff");
        await using var otherDb = CreateDb(databaseName, otherOperator);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetInventoryAdjustmentDetailHandler(otherDb, otherOperator, new FakeAdjustmentPermissions(otherOperator))
                .Handle(new GetInventoryAdjustmentDetailQuery(submitted.Id), CancellationToken.None));
    }

    [Fact]
    public async Task SupervisorApproval_OfZeroCount_RemovesEmptyBatchAndKeepsAuditSnapshot()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requester = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var operatorDb = CreateDb(databaseName, requester);
        var batchId = await SeedAsync(operatorDb, 12);
        var submitted = await new SubmitInventoryAdjustmentHandler(operatorDb, new AdjustmentClock(), requester, new AdjustmentNotifications(), new FakeAdjustmentPermissions(requester))
            .Handle(new SubmitInventoryAdjustmentCommand(batchId, 0, InventoryAdjustmentReasonCodes.DamagedStock, "All boxes damaged"), CancellationToken.None);

        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var supervisorDb = CreateDb(databaseName, supervisor);
        var approved = await new ApproveInventoryAdjustmentHandler(supervisorDb, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id, "Damage verified"), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Approved, approved.Status);
        Assert.False(supervisorDb.StockBatches.Any(b => b.Id == batchId));
        Assert.Equal(12, approved.ActualQuantityBeforePosting);
        Assert.Equal(0, approved.ActualQuantityAfterPosting);
    }

    private static AppDbContext CreateDb(string databaseName, ICurrentUserService user)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new AppDbContext(options, user, new AdjustmentClock());
    }

    [Fact]
    public async Task PkmCorrection_OfTheSameMaterial_ReplacesTheSystemPkmAfterApproval_AndLedgersBothPkms()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requester = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var operatorDb = CreateDb(databaseName, requester);
        var batchId = await SeedAsync(operatorDb, 40);                       // 40 boxes recorded as PKM OCT|2026

        var submitted = await new SubmitInventoryAdjustmentHandler(
                operatorDb, new AdjustmentClock(), requester, new AdjustmentNotifications(), new FakeAdjustmentPermissions(requester))
            .Handle(new SubmitInventoryAdjustmentCommand(
                batchId, 40, InventoryAdjustmentReasonCodes.DataEntryCorrection, "Boxes are marked SEP|2026", PhysicalMfgMonth: 202609), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.PendingApproval, submitted.Status);
        Assert.Equal("SEP|2026", submitted.PhysicalMfgMonthLabel);
        Assert.Null(submitted.PhysicalMaterialNumber);
        Assert.Equal(40, operatorDb.StockBatches.Single().QuantityBoxes);   // nothing changes until approval
        Assert.Equal(202610, operatorDb.StockBatches.Single().MfgMonth);

        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var supervisorDb = CreateDb(databaseName, supervisor);
        var approved = await new ApproveInventoryAdjustmentHandler(
                supervisorDb, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Approved, approved.Status);
        var stock = supervisorDb.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes > 0).ToList();
        var replacement = Assert.Single(stock);
        Assert.Equal(202609, replacement.MfgMonth);
        Assert.Equal(40, replacement.QuantityBoxes);
        Assert.DoesNotContain(supervisorDb.StockBatches.AsNoTracking(), b => b.MfgMonth == 202610);
        var ledger = supervisorDb.StockLedgerEntries.AsNoTracking().OrderBy(e => e.Id).ToList();
        Assert.Equal(2, ledger.Count);
        Assert.Contains(ledger, e => e.MfgMonth == 202610 && e.QuantityBoxes == -40 && e.BalanceAfterBoxes == 0);
        Assert.Contains(ledger, e => e.MfgMonth == 202609 && e.QuantityBoxes == 40 && e.BalanceAfterBoxes == 40);
    }

    [Fact]
    public async Task PkmAndSkuCorrection_Together_MoveTheBoxesToTheFoundSkuAndPkm_MergingIntoAnExistingBatch()
    {
        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var db = CreateDb(Guid.NewGuid().ToString(), supervisor);
        var batchId = await SeedAsync(db, 40);
        var found = NewMaterial(40039574, "Found SKU");
        db.Materials.Add(found);
        await db.SaveChangesAsync();
        var position = db.StockBatches.Single().PalletPositionId;
        db.StockBatches.Add(new StockBatch { MaterialId = found.Id, PalletPositionId = position, MfgMonth = 202608, StockSubtypeId = LocationSubtypeIds.Good, QuantityBoxes = 5, RowVersion = 1 });
        await db.SaveChangesAsync();

        var result = await new SubmitInventoryAdjustmentHandler(
                db, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new SubmitInventoryAdjustmentCommand(
                batchId, 38, InventoryAdjustmentReasonCodes.DataEntryCorrection, "Different SKU and PKM", found.Id, 202608), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.Approved, result.Status);        // a supervisor approves their own request
        var live = db.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes > 0).Single();
        Assert.Equal((found.Id, 202608, 38m), (live.MaterialId, live.MfgMonth, live.QuantityBoxes));   // the found stock is set to what was counted
        Assert.DoesNotContain(db.StockBatches.AsNoTracking(), b => b.MaterialId != found.Id);          // the wrongly recorded batch is gone
    }

    [Fact]
    public async Task PkmCorrection_NeedsAQuantity_AValidMonth_AndNoOtherPendingRequestOnTheCell()
    {
        var requester = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var db = CreateDb(Guid.NewGuid().ToString(), requester);
        var batchId = await SeedAsync(db, 40);
        var validator = new SubmitInventoryAdjustmentValidator();
        Assert.False(validator.Validate(new SubmitInventoryAdjustmentCommand(batchId, 0, InventoryAdjustmentReasonCodes.DataEntryCorrection, null, PhysicalMfgMonth: 202609)).IsValid);
        Assert.False(validator.Validate(new SubmitInventoryAdjustmentCommand(batchId, 5, InventoryAdjustmentReasonCodes.DataEntryCorrection, null, PhysicalMfgMonth: 202613)).IsValid);
        Assert.True(validator.Validate(new SubmitInventoryAdjustmentCommand(batchId, 5, InventoryAdjustmentReasonCodes.DataEntryCorrection, null, PhysicalMfgMonth: 202609)).IsValid);

        var handler = new SubmitInventoryAdjustmentHandler(db, new AdjustmentClock(), requester, new AdjustmentNotifications(), new FakeAdjustmentPermissions(requester));
        // the same PKM as the system record and the same quantity is no adjustment at all
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new SubmitInventoryAdjustmentCommand(batchId, 40, InventoryAdjustmentReasonCodes.DataEntryCorrection, null, PhysicalMfgMonth: 202610), CancellationToken.None));
        await handler.Handle(new SubmitInventoryAdjustmentCommand(batchId, 40, InventoryAdjustmentReasonCodes.DataEntryCorrection, null, PhysicalMfgMonth: 202609), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new SubmitInventoryAdjustmentCommand(batchId, 40, InventoryAdjustmentReasonCodes.DataEntryCorrection, null, PhysicalMfgMonth: 202608), CancellationToken.None));
    }

    [Fact]
    public async Task StockTypeCorrection_MovesTheBoxesFromGoodToDamage_AfterApproval_AndLedgersBothStockTypes()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requester = new AdjustmentUser("operator", "operator@test", "Office Staff");
        await using var operatorDb = CreateDb(databaseName, requester);
        var batchId = await SeedAsync(operatorDb, 40);
        operatorDb.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Damage, Code = "Damage", DisplayName = "Damage" });
        await operatorDb.SaveChangesAsync();
        var position = operatorDb.StockBatches.Single().PalletPositionId;
        var material = operatorDb.StockBatches.Single().MaterialId;
        // damaged boxes of the same material and PKM already sit in the cell
        operatorDb.StockBatches.Add(new StockBatch { MaterialId = material, PalletPositionId = position, MfgMonth = 202610, StockSubtypeId = LocationSubtypeIds.Damage, QuantityBoxes = 2, RowVersion = 1 });
        await operatorDb.SaveChangesAsync();

        var validator = new SubmitInventoryAdjustmentValidator();
        Assert.False(validator.Validate(new SubmitInventoryAdjustmentCommand(batchId, 0, InventoryAdjustmentReasonCodes.DamagedStock, null, PhysicalStockSubtypeId: LocationSubtypeIds.Damage)).IsValid);
        Assert.False(validator.Validate(new SubmitInventoryAdjustmentCommand(batchId, 5, InventoryAdjustmentReasonCodes.DamagedStock, null, PhysicalStockSubtypeId: LocationSubtypeIds.Expire)).IsValid);

        var submitted = await new SubmitInventoryAdjustmentHandler(
                operatorDb, new AdjustmentClock(), requester, new AdjustmentNotifications(), new FakeAdjustmentPermissions(requester))
            .Handle(new SubmitInventoryAdjustmentCommand(
                batchId, 40, InventoryAdjustmentReasonCodes.DamagedStock, "All 40 boxes are damaged", PhysicalStockSubtypeId: LocationSubtypeIds.Damage), CancellationToken.None);

        Assert.Equal(InventoryAdjustmentStatus.PendingApproval, submitted.Status);
        Assert.Equal("Damage", submitted.PhysicalStockSubtypeName);
        Assert.Equal(40, operatorDb.StockBatches.Single(b => b.StockSubtypeId == LocationSubtypeIds.Good).QuantityBoxes);   // nothing changes before approval

        var supervisor = new AdjustmentUser("supervisor", "supervisor@test", "Supervisor");
        await using var supervisorDb = CreateDb(databaseName, supervisor);
        await new ApproveInventoryAdjustmentHandler(
                supervisorDb, new AdjustmentClock(), supervisor, new AdjustmentNotifications(), new FakeAdjustmentPermissions(supervisor))
            .Handle(new ApproveInventoryAdjustmentCommand(submitted.Id), CancellationToken.None);

        Assert.DoesNotContain(supervisorDb.StockBatches.AsNoTracking(), b => b.StockSubtypeId == LocationSubtypeIds.Good);          // the Good batch is gone
        Assert.Equal(40, supervisorDb.StockBatches.AsNoTracking().Single(b => b.StockSubtypeId == LocationSubtypeIds.Damage).QuantityBoxes);   // set to what was counted
        var ledger = supervisorDb.StockLedgerEntries.AsNoTracking().ToList();
        Assert.Contains(ledger, e => e.StockSubtypeId == LocationSubtypeIds.Good && e.QuantityBoxes == -40 && e.BalanceAfterBoxes == 0);
        Assert.Contains(ledger, e => e.StockSubtypeId == LocationSubtypeIds.Damage && e.QuantityBoxes == 38 && e.BalanceAfterBoxes == 40);
    }

    private static async Task<int> SeedAsync(AppDbContext db, decimal quantity)
    {
        var subtype = new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "Good", DisplayName = "Good" };
        var rack = new Rack { WarehouseId = 1, Code = "C", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "C-01-01", StockBatches = [] };
        rack.PalletPositions.Add(position);
        var material = NewMaterial(40019311, "Test SKU");
        db.LocationSubtypes.Add(subtype);
        db.Racks.Add(rack);
        db.Materials.Add(material);
        // Every fake persona these tests use is assumed to be assigned to warehouse 1 - matching
        // the real app's Phase 3 backfill for pre-existing accounts, now that only Superadmin
        // bypasses the UserWarehouse/UserRegion query filter.
        foreach (var userId in new[] { "operator", "operator-1", "operator-2", "supervisor" })
        {
            db.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = 1 });
        }
        await db.SaveChangesAsync();
        var batch = new StockBatch
        {
            MaterialId = material.Id,
            PalletPositionId = position.Id,
            MfgMonth = 202610,
            StockSubtypeId = subtype.Id,
            QuantityBoxes = quantity,
            RowVersion = 1
        };
        db.StockBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch.Id;
    }

    private static Material NewMaterial(long materialNumber, string description) => new()
    {
        MaterialNumber = materialNumber,
        Description = description,
        DesignType = "GGAF1",
        PackSize = 1,
        GrossWeightKg = 1,
        LengthMm = 1,
        WidthMm = 1,
        HeightMm = 1,
        PalletCapacityBoxes = 60,
        SeasonId = SeasonIds.Rainy
    };
}

file sealed class AdjustmentUser(string id, string name, string role) : ICurrentUserService
{
    public string? UserId => id;
    public string? UserName => name;
    public bool IsInRole(string requestedRole) => requestedRole == role;
    public IReadOnlyList<string> Roles => [role];
}

/// <summary>Mirrors the real RolePermission seed grants for the keys these tests exercise:
/// Supervisor (and Superadmin) can approve/reject, Office Staff can only submit/view its own.</summary>
file sealed class FakeAdjustmentPermissions(ICurrentUserService user) : IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default)
    {
        var isReviewer = user.Roles.Contains("Supervisor") || user.Roles.Contains("Superadmin");
        var granted = permissionKey == PermissionKeys.Inventory.ApproveAdjustment ? isReviewer : true;
        return Task.FromResult(granted);
    }
}

file sealed class AdjustmentClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow => new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    public DateOnly Today => new(2026, 10, 1);
}

file sealed class AdjustmentNotifications : IRealtimeNotificationService
{
    public List<RealtimeNotification> Published { get; } = [];

    public IDisposable Subscribe(string? userId, IReadOnlyCollection<string> roles, Func<RealtimeNotification, Task> handler) =>
        new NoopDisposable();

    public Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
