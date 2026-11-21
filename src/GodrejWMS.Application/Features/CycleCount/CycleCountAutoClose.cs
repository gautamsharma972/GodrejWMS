using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount;

/// <summary>Closing a cycle count by itself, so the Inward / Pullout freeze never outlives the work: when every rack has been submitted and
/// reviewed, and (for a batch scheduled to release the freeze at its end) when Schedule To has passed.</summary>
public static class CycleCountAutoClose
{
    public const string SystemUserName = "System (auto-close)";

    /// <summary>True when nothing is left to count or review: no rack is still open and every submitted rack's review is done.</summary>
    public static async Task<bool> IsCompletelyReviewedAsync(IApplicationDbContext db, CycleCountBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Racks.Any(r => r.Status == CycleCountRackStatus.Open)) return false;
        var reviews = await db.CycleCountReviews.AsNoTracking().Where(r => r.CycleCountBatchId == batch.Id).Select(r => r.Status).ToListAsync(cancellationToken);
        return reviews.Count > 0 && reviews.All(s => s == CycleCountReviewStatus.Reviewed);
    }

    /// <summary>Closes an open batch whose racks are all submitted (or excluded) and reviewed. Returns true when it closed it.</summary>
    public static async Task<bool> CloseIfReviewedAsync(
        IApplicationDbContext db, CycleCountBatch batch, ICurrentUserService user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!CycleCountBatch.IsOpen(batch.Status) || !await IsCompletelyReviewedAsync(db, batch, cancellationToken)) return false;
        var previous = batch.Status;
        batch.Status = CycleCountBatchStatus.Closed;
        batch.ActualEndAt = now;
        CycleCountRules.Touch(batch, user, now);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.AutoClosed, user, now, previous.ToString(), CycleCountBatchStatus.Closed.ToString(),
            "Every rack was submitted and reviewed, so the cycle count was closed automatically.");
        return true;
    }

    /// <summary>Closes every open batch that asked for its freeze to end with Schedule To and whose Schedule To has passed. Racks that were
    /// not submitted are excluded (their uncounted cards and unsubmitted counts are never applied to stock); submitted racks stay as they are.
    /// Returns the batches closed. Runs without a signed-in user (the background service), so it reads across warehouses.</summary>
    public static async Task<IReadOnlyList<CycleCountBatch>> CloseEndedBatchesAsync(IApplicationDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var batches = await db.CycleCountBatches.IgnoreQueryFilters().Include(b => b.Racks)
            .Where(b => b.AutoReleaseFreezeAtScheduleEnd && b.ScheduleTo <= now
                && (b.Status == CycleCountBatchStatus.Scheduled || b.Status == CycleCountBatchStatus.InProgress || b.Status == CycleCountBatchStatus.Extended
                    || b.Status == CycleCountBatchStatus.PartiallyClosed || b.Status == CycleCountBatchStatus.PendingReview))
            .ToListAsync(cancellationToken);
        var system = new SystemUser();
        var closed = new List<CycleCountBatch>();
        foreach (var batch in batches)
        {
            var openRacks = batch.Racks.Where(r => r.Status == CycleCountRackStatus.Open).ToList();
            var openCodes = openRacks.Select(r => r.RackCode.ToUpperInvariant()).ToHashSet();
            var cards = openRacks.Count == 0
                ? []
                : await db.CycleCountCards.IgnoreQueryFilters()
                    .Where(c => c.CycleCountBatchId == batch.Id && c.SubmittedAt == null).ToListAsync(cancellationToken);
            var inOpenRacks = cards.Where(c => openCodes.Contains(c.RackCode.ToUpperInvariant())).ToList();
            var pendingCards = inOpenRacks.Count(c => !c.IsExcluded && c.State == CycleCountCardState.Pending);
            var draftCards = inOpenRacks.Count(c => !c.IsExcluded && c.State != CycleCountCardState.Pending);
            foreach (var card in inOpenRacks.Where(c => !c.IsExcluded && c.State == CycleCountCardState.Pending))
            {
                card.IsExcluded = true;
                card.RowVersion++;
            }

            foreach (var rack in openRacks)
            {
                rack.Status = CycleCountRackStatus.Excluded;
                rack.RowVersion++;
            }

            var previous = batch.Status;
            batch.Status = CycleCountBatchStatus.Closed;
            batch.ActualEndAt = now;
            CycleCountRules.Touch(batch, system, now);
            CycleCountRules.AddHistory(db, batch, CycleCountActions.AutoClosed, system, now, previous.ToString(), CycleCountBatchStatus.Closed.ToString(),
                openRacks.Count == 0
                    ? "Schedule To has passed, so the cycle count was closed automatically."
                    : $"Schedule To has passed, so the cycle count was closed automatically. {openRacks.Count} rack(s) were not submitted and were excluded: {pendingCards} uncounted card(s) and {draftCards} counted but unsubmitted card(s) were not applied to stock.");
            closed.Add(batch);
        }

        if (closed.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return closed;
    }

    private sealed class SystemUser : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => SystemUserName;
        public bool IsInRole(string role) => false;
        public IReadOnlyList<string> Roles => [];
    }
}
