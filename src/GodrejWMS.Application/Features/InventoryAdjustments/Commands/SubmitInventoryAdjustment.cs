using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.InventoryAdjustments.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryAdjustments.Commands;

public sealed record SubmitInventoryAdjustmentCommand(
    int StockBatchId,
    int CountedQuantity,
    string ReasonCode,
    string? ReasonText,
    int? PhysicalMaterialId = null) : IRequest<InventoryAdjustmentDetailDto>;

public sealed class SubmitInventoryAdjustmentValidator : AbstractValidator<SubmitInventoryAdjustmentCommand>
{
    public SubmitInventoryAdjustmentValidator()
    {
        RuleFor(x => x.StockBatchId).GreaterThan(0);
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CountedQuantity).GreaterThan(0)
            .When(x => x.PhysicalMaterialId.HasValue)
            .WithMessage("Physical quantity must be greater than zero when correcting the SKU.");
        RuleFor(x => x.PhysicalMaterialId).GreaterThan(0).When(x => x.PhysicalMaterialId.HasValue);
        RuleFor(x => x.ReasonCode)
            .Must(InventoryAdjustmentReasonCodes.All.ContainsKey)
            .WithMessage("Select a valid adjustment reason.");
        RuleFor(x => x.ReasonText).MaximumLength(500);
        RuleFor(x => x.ReasonText)
            .NotEmpty()
            .When(x => x.ReasonCode == InventoryAdjustmentReasonCodes.Other)
            .WithMessage("Explain the adjustment when the reason is Other.");
    }
}

public sealed class SubmitInventoryAdjustmentHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser,
    IRealtimeNotificationService notifications,
    IPermissionService permissions)
    : IRequestHandler<SubmitInventoryAdjustmentCommand, InventoryAdjustmentDetailDto>
{
    public async Task<InventoryAdjustmentDetailDto> Handle(SubmitInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inventory.Adjust, cancellationToken))
        {
            throw new UnauthorizedAccessException("You are not permitted to request inventory adjustments.");
        }

        var batch = await db.StockBatches
            .Include(b => b.Material)
            .Include(b => b.PalletPosition).ThenInclude(p => p.Rack)
            .Include(b => b.StockSubtype)
            .FirstOrDefaultAsync(b => b.Id == request.StockBatchId, cancellationToken)
            ?? throw new InvalidOperationException("The selected inventory batch was not found.");

        var physicalMaterialId = request.PhysicalMaterialId == batch.MaterialId ? null : request.PhysicalMaterialId;
        Material? physicalMaterial = null;
        StockBatch? physicalBatch = null;
        if (physicalMaterialId.HasValue)
        {
            physicalMaterial = await db.Materials.FirstOrDefaultAsync(
                m => m.Id == physicalMaterialId.Value && m.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("The physically counted SKU was not found or is inactive.");
            physicalBatch = await db.StockBatches.FirstOrDefaultAsync(b =>
                b.MaterialId == physicalMaterialId.Value &&
                b.PalletPositionId == batch.PalletPositionId &&
                b.MfgMonth == batch.MfgMonth, cancellationToken);
        }

        if (!physicalMaterialId.HasValue && batch.QuantityBoxes == request.CountedQuantity)
        {
            throw new InvalidOperationException("The physical quantity matches the system quantity; no adjustment is required.");
        }

        var hasPendingRequest = await db.InventoryAdjustmentLines
            .AnyAsync(l => l.StockBatchId == batch.Id && l.InventoryAdjustment.Status == InventoryAdjustmentStatus.PendingApproval, cancellationToken);
        if (hasPendingRequest)
        {
            throw new InvalidOperationException("A pending adjustment already exists for this SKU, PKM, and location.");
        }

        if (physicalBatch is not null && await db.InventoryAdjustmentLines.AnyAsync(l =>
                l.StockBatchId == physicalBatch.Id &&
                l.InventoryAdjustment.Status == InventoryAdjustmentStatus.PendingApproval, cancellationToken))
        {
            throw new InvalidOperationException("A pending adjustment already exists for the physically counted SKU in this location.");
        }

        var now = clock.UtcNow;
        var adjustment = new InventoryAdjustment
        {
            AdjustmentNumber = $"ADJ-{now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}",
            WarehouseId = batch.PalletPosition.Rack.WarehouseId,
            Status = InventoryAdjustmentStatus.PendingApproval,
            OpenStockBatchId = batch.Id,
            ReasonCode = request.ReasonCode,
            ReasonText = string.IsNullOrWhiteSpace(request.ReasonText) ? null : request.ReasonText.Trim(),
            RequestedByUserId = currentUser.UserId,
            RequestedByUserName = currentUser.UserName,
            RequestedAt = now,
            RowVersion = 1
        };
        adjustment.Lines.Add(new InventoryAdjustmentLine
        {
            StockBatchId = batch.Id,
            MaterialId = batch.MaterialId,
            PhysicalMaterialId = physicalMaterialId,
            PalletPositionId = batch.PalletPositionId,
            MfgMonth = batch.MfgMonth,
            StockSubtypeId = batch.StockSubtypeId,
            SystemQuantityAtCount = batch.QuantityBoxes,
            CountedQuantity = request.CountedQuantity,
            VarianceQuantity = request.CountedQuantity - batch.QuantityBoxes,
            BatchRowVersionAtCount = batch.RowVersion,
            PhysicalStockBatchIdAtCount = physicalBatch?.Id,
            PhysicalSystemQuantityAtCount = physicalBatch?.QuantityBoxes,
            PhysicalBatchRowVersionAtCount = physicalBatch?.RowVersion
        });

        db.InventoryAdjustments.Add(adjustment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("A pending adjustment was created for this inventory while you were submitting. Refresh and review the existing request.");
        }

        // A user who already holds approval authority (normally the Supervisor) is the final
        // reviewer in this workflow. Apply the same approval handler immediately so their own
        // count is posted without creating a request that nobody else can approve.
        if (await permissions.HasPermissionAsync(PermissionKeys.Inventory.ApproveAdjustment, cancellationToken))
        {
            var approvalHandler = new ApproveInventoryAdjustmentHandler(
                db, clock, currentUser, notifications, permissions);
            return await approvalHandler.Handle(
                new ApproveInventoryAdjustmentCommand(adjustment.Id, "Auto-approved for authorized inventory reviewer."),
                cancellationToken);
        }

        await notifications.PublishAsync(new RealtimeNotification(
            Guid.NewGuid(),
            "Adjustment approval requested",
            physicalMaterial is null
                ? $"{adjustment.AdjustmentNumber}: SKU {batch.Material.MaterialNumber} at {batch.PalletPosition.LocationCode} requires review."
                : $"{adjustment.AdjustmentNumber}: SKU {batch.Material.MaterialNumber} was physically identified as {physicalMaterial.MaterialNumber} at {batch.PalletPosition.LocationCode}.",
            "warning",
            "/inventory-adjustments",
            now,
            AudienceRoles: ["Supervisor", "Superadmin"],
            ExcludeUserId: currentUser.UserId), cancellationToken);

        await PublishRefreshAsync(RealtimeTopics.InventoryAdjustmentsChanged, now, cancellationToken);

        return await LoadDetailAsync(adjustment.Id, cancellationToken);
    }

    private Task PublishRefreshAsync(string topic, DateTimeOffset now, CancellationToken cancellationToken) =>
        notifications.PublishAsync(new RealtimeNotification(
            Guid.NewGuid(), string.Empty, string.Empty, "info", null, now,
            Topic: topic, Silent: true, Broadcast: true), cancellationToken);

    private async Task<InventoryAdjustmentDetailDto> LoadDetailAsync(int id, CancellationToken cancellationToken)
    {
        var adjustment = await db.InventoryAdjustments.AsNoTracking()
            .Include(a => a.Lines).ThenInclude(l => l.Material)
            .Include(a => a.Lines).ThenInclude(l => l.PhysicalMaterial)
            .Include(a => a.Lines).ThenInclude(l => l.PalletPosition)
            .Include(a => a.Lines).ThenInclude(l => l.StockSubtype)
            .SingleAsync(a => a.Id == id, cancellationToken);
        return InventoryAdjustmentMapper.ToDetail(adjustment);
    }
}
