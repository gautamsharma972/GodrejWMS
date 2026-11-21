using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Commands;

/// <summary>The Head Supervisor's verification of a submitted rack. An audit step only: stock was already updated at submission.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Review)]
public sealed record MarkCycleCountReviewedCommand(int BatchId, string RackCode, string? Remarks = null) : IRequest<bool>;


public sealed class MarkCycleCountReviewedHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<MarkCycleCountReviewedCommand, bool>
{
    public async Task<bool> Handle(MarkCycleCountReviewedCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var rack = CycleCountCounting.FindRack(batch, request.RackCode);
        var review = await db.CycleCountReviews.FirstOrDefaultAsync(r => r.CycleCountBatchId == batch.Id && r.RackCode == rack.RackCode, cancellationToken)
            ?? throw new InvalidOperationException("This rack has not been submitted yet, so there is nothing to review.");
        if (review.Status == CycleCountReviewStatus.Reviewed) return false; // already reviewed: nothing to do

        var now = clock.UtcNow;
        review.Status = CycleCountReviewStatus.Reviewed;
        review.ReviewedAt = now;
        review.ReviewedByUserId = user.UserId;
        review.ReviewedByUserName = user.UserName;
        review.ReviewRemarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        review.UpdatedAt = now;
        review.UpdatedByUserId = user.UserId;
        CycleCountRules.AddHistory(db, batch, CycleCountActions.Reviewed, user, now, CycleCountReviewStatus.PendingReview.ToString(), CycleCountReviewStatus.Reviewed.ToString(),
            review.ReviewRemarks, rack.RackCode);
        await db.SaveChangesAsync(cancellationToken);

        // Every rack submitted and reviewed: the cycle count is complete, so close it and let Inward / Pullout run again.
        var closed = await CycleCountAutoClose.CloseIfReviewedAsync(db, batch, user, now, cancellationToken);
        if (closed)
        {
            await db.SaveChangesAsync(cancellationToken);
            await CycleCountNotify.PublishAsync(notifications, now, "Cycle count closed",
                $"{batch.BatchNumber} is closed automatically: every rack was reviewed. Inward and Pullout creation is allowed again for the warehouse unless another cycle count is open.", excludeUserId: user.UserId, cancellationToken: cancellationToken);
        }

        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count reviewed", $"{batch.BatchNumber} rack {rack.RackCode} was reviewed.", url: "/cycle-count/review", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return true;
    }
}
