using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.InventoryAdjustments.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryAdjustments.Commands;

public sealed record ApproveInventoryAdjustmentCommand(int AdjustmentId, string? Comment = null)
    : IRequest<InventoryAdjustmentDetailDto>;

public sealed record RejectInventoryAdjustmentCommand(int AdjustmentId, string Comment)
    : IRequest<InventoryAdjustmentDetailDto>;

public sealed record CancelInventoryAdjustmentCommand(int AdjustmentId)
    : IRequest<InventoryAdjustmentDetailDto>;

public sealed class ApproveInventoryAdjustmentValidator : AbstractValidator<ApproveInventoryAdjustmentCommand>
{
    public ApproveInventoryAdjustmentValidator()
    {
        RuleFor(x => x.AdjustmentId).GreaterThan(0);
        RuleFor(x => x.Comment).MaximumLength(500);
    }
}

public sealed class RejectInventoryAdjustmentValidator : AbstractValidator<RejectInventoryAdjustmentCommand>
{
    public RejectInventoryAdjustmentValidator()
    {
        RuleFor(x => x.AdjustmentId).GreaterThan(0);
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(500);
    }
}

public sealed class CancelInventoryAdjustmentValidator : AbstractValidator<CancelInventoryAdjustmentCommand>
{
    public CancelInventoryAdjustmentValidator() => RuleFor(x => x.AdjustmentId).GreaterThan(0);
}

public sealed class ApproveInventoryAdjustmentHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser,
    IRealtimeNotificationService notifications)
    : IRequestHandler<ApproveInventoryAdjustmentCommand, InventoryAdjustmentDetailDto>
{
    public async Task<InventoryAdjustmentDetailDto> Handle(ApproveInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        EnsureReviewer(currentUser);
        var adjustment = await LoadTrackedAsync(db, request.AdjustmentId, cancellationToken);
        EnsurePending(adjustment);

        if (!string.IsNullOrWhiteSpace(adjustment.RequestedByUserId) && adjustment.RequestedByUserId == currentUser.UserId)
        {
            throw new InvalidOperationException("You cannot approve your own inventory adjustment request.");
        }

        var line = adjustment.Lines.Single();
        var batch = line.StockBatchId.HasValue
            ? await db.StockBatches.FirstOrDefaultAsync(b => b.Id == line.StockBatchId.Value, cancellationToken)
            : null;

        if (batch is null || batch.RowVersion != line.BatchRowVersionAtCount || batch.QuantityBoxes != line.SystemQuantityAtCount)
        {
            line.ActualQuantityBeforePosting = batch?.QuantityBoxes;
            adjustment.Status = InventoryAdjustmentStatus.Conflict;
            adjustment.OpenStockBatchId = null;
            adjustment.ReviewedByUserId = currentUser.UserId;
            adjustment.ReviewedByUserName = currentUser.UserName;
            adjustment.ReviewedAt = clock.UtcNow;
            adjustment.ReviewComment = "Inventory changed after this count was submitted. A new count is required.";
            adjustment.RowVersion++;
            await db.SaveChangesAsync(cancellationToken);
            await NotifyRequesterAsync(adjustment, "Adjustment needs a recount",
                $"{adjustment.AdjustmentNumber} could not be approved because inventory changed after the count.",
                "warning", cancellationToken);
            return await LoadDetailAsync(db, adjustment.Id, cancellationToken);
        }

        line.ActualQuantityBeforePosting = batch.QuantityBoxes;
        line.ActualQuantityAfterPosting = line.CountedQuantity;
        if (line.CountedQuantity == 0)
        {
            line.StockBatchId = null;
            line.StockBatch = null;
            db.StockBatches.Remove(batch);
        }
        else
        {
            batch.QuantityBoxes = line.CountedQuantity;
            batch.RowVersion++;
        }

        adjustment.Status = InventoryAdjustmentStatus.Approved;
        adjustment.OpenStockBatchId = null;
        adjustment.ReviewedByUserId = currentUser.UserId;
        adjustment.ReviewedByUserName = currentUser.UserName;
        adjustment.ReviewedAt = clock.UtcNow;
        adjustment.ReviewComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        adjustment.RowVersion++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Inventory changed while the adjustment was being approved. Refresh and submit a new physical count.");
        }

        await NotifyRequesterAsync(adjustment, "Adjustment approved",
            $"{adjustment.AdjustmentNumber} was approved and live inventory was updated.",
            "success", cancellationToken);

        return await LoadDetailAsync(db, adjustment.Id, cancellationToken);
    }

    private static void EnsureReviewer(ICurrentUserService user)
    {
        if (!user.IsInRole("Supervisor") && !user.IsInRole("Admin"))
        {
            throw new UnauthorizedAccessException("Only a Supervisor or Admin can approve inventory adjustments.");
        }
    }

    internal static void EnsurePending(InventoryAdjustment adjustment)
    {
        if (adjustment.Status != InventoryAdjustmentStatus.PendingApproval)
        {
            throw new InvalidOperationException($"This adjustment is already {adjustment.Status}.");
        }
    }

    internal static async Task<InventoryAdjustment> LoadTrackedAsync(IApplicationDbContext db, int id, CancellationToken cancellationToken) =>
        await db.InventoryAdjustments
            .Include(a => a.Lines)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(InventoryAdjustment), id);

    internal static async Task<InventoryAdjustmentDetailDto> LoadDetailAsync(IApplicationDbContext db, int id, CancellationToken cancellationToken)
    {
        var adjustment = await db.InventoryAdjustments.AsNoTracking()
            .Include(a => a.Lines).ThenInclude(l => l.Material)
            .Include(a => a.Lines).ThenInclude(l => l.PalletPosition)
            .Include(a => a.Lines).ThenInclude(l => l.StockSubtype)
            .SingleAsync(a => a.Id == id, cancellationToken);
        return InventoryAdjustmentMapper.ToDetail(adjustment);
    }

    private Task NotifyRequesterAsync(
        InventoryAdjustment adjustment,
        string title,
        string message,
        string kind,
        CancellationToken cancellationToken) =>
        notifications.PublishAsync(new RealtimeNotification(
            Guid.NewGuid(), title, message, kind, "/inventory-adjustments", clock.UtcNow,
            RecipientUserId: adjustment.RequestedByUserId), cancellationToken);
}

public sealed class RejectInventoryAdjustmentHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser,
    IRealtimeNotificationService notifications)
    : IRequestHandler<RejectInventoryAdjustmentCommand, InventoryAdjustmentDetailDto>
{
    public async Task<InventoryAdjustmentDetailDto> Handle(RejectInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole("Supervisor") && !currentUser.IsInRole("Admin"))
        {
            throw new UnauthorizedAccessException("Only a Supervisor or Admin can reject inventory adjustments.");
        }

        var adjustment = await ApproveInventoryAdjustmentHandler.LoadTrackedAsync(db, request.AdjustmentId, cancellationToken);
        ApproveInventoryAdjustmentHandler.EnsurePending(adjustment);
        if (!string.IsNullOrWhiteSpace(adjustment.RequestedByUserId) && adjustment.RequestedByUserId == currentUser.UserId)
        {
            throw new InvalidOperationException("You cannot review your own inventory adjustment request.");
        }

        adjustment.Status = InventoryAdjustmentStatus.Rejected;
        adjustment.OpenStockBatchId = null;
        adjustment.ReviewedByUserId = currentUser.UserId;
        adjustment.ReviewedByUserName = currentUser.UserName;
        adjustment.ReviewedAt = clock.UtcNow;
        adjustment.ReviewComment = request.Comment.Trim();
        adjustment.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.PublishAsync(new RealtimeNotification(
            Guid.NewGuid(),
            "Adjustment rejected",
            $"{adjustment.AdjustmentNumber} was rejected: {adjustment.ReviewComment}",
            "danger",
            "/inventory-adjustments",
            clock.UtcNow,
            RecipientUserId: adjustment.RequestedByUserId), cancellationToken);
        return await ApproveInventoryAdjustmentHandler.LoadDetailAsync(db, adjustment.Id, cancellationToken);
    }
}

public sealed class CancelInventoryAdjustmentHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser,
    IRealtimeNotificationService notifications)
    : IRequestHandler<CancelInventoryAdjustmentCommand, InventoryAdjustmentDetailDto>
{
    public async Task<InventoryAdjustmentDetailDto> Handle(CancelInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var adjustment = await ApproveInventoryAdjustmentHandler.LoadTrackedAsync(db, request.AdjustmentId, cancellationToken);
        ApproveInventoryAdjustmentHandler.EnsurePending(adjustment);
        if (adjustment.RequestedByUserId != currentUser.UserId && !currentUser.IsInRole("Admin"))
        {
            throw new UnauthorizedAccessException("You can only cancel your own pending adjustment requests.");
        }

        adjustment.Status = InventoryAdjustmentStatus.Cancelled;
        adjustment.OpenStockBatchId = null;
        adjustment.ReviewedByUserId = currentUser.UserId;
        adjustment.ReviewedByUserName = currentUser.UserName;
        adjustment.ReviewedAt = clock.UtcNow;
        adjustment.ReviewComment = "Cancelled by requester.";
        adjustment.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await notifications.PublishAsync(new RealtimeNotification(
            Guid.NewGuid(),
            "Adjustment request cancelled",
            $"{adjustment.AdjustmentNumber} was cancelled by {currentUser.UserName ?? "the requester"}.",
            "info",
            "/inventory-adjustments",
            clock.UtcNow,
            AudienceRoles: ["Supervisor", "Admin"],
            ExcludeUserId: currentUser.UserId), cancellationToken);
        return await ApproveInventoryAdjustmentHandler.LoadDetailAsync(db, adjustment.Id, cancellationToken);
    }
}
