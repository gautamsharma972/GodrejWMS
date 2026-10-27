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
        var reviewNotification = Assert.Single(submitNotifications.Published);
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
        var requesterNotification = Assert.Single(approvalNotifications.Published);
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

    private static async Task<int> SeedAsync(AppDbContext db, decimal quantity)
    {
        var subtype = new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "Good", DisplayName = "Good" };
        var rack = new Rack { WarehouseId = 1, Code = "C", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "C-01-01", StockBatches = [] };
        rack.PalletPositions.Add(position);
        var material = new Material
        {
            MaterialNumber = 40019311,
            Description = "Test SKU",
            DesignType = "GGAF1",
            PackSize = 1,
            GrossWeightKg = 1,
            LengthMm = 1,
            WidthMm = 1,
            HeightMm = 1,
            PalletCapacityBoxes = 60,
            SeasonId = SeasonIds.Rainy
        };
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
