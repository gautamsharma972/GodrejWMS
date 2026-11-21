using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Commands;

/// <summary>Submits the counted cards of one rack: the system stock of every location counted is set to what was physically found, through the
/// shared stock posting service (ledger type CYCLE_COUNT, reference = the batch number), in one transaction. Submitting twice posts once.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Submit)]
public sealed record SubmitCycleCountRackCommand(int BatchId, string RackCode) : IRequest<CycleCountSubmitResultDto>;

public sealed class SubmitCycleCountRackHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IStockPostingService posting, IUnitOfWork uow,
    IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<SubmitCycleCountRackCommand, CycleCountSubmitResultDto>
{
    public async Task<CycleCountSubmitResultDto> Handle(SubmitCycleCountRackCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);
            if (!result.AlreadySubmitted)
            {
                var now = clock.UtcNow;
                await CycleCountNotify.PublishAsync(notifications, now, "Cycle count submitted",
                    $"{result.BatchNumber} rack {result.RackCode} was submitted ({result.CorrectedCards} corrected card(s)) and is waiting for review.", url: "/cycle-count/review", cancellationToken: cancellationToken, excludeUserId: user.UserId);
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A second submit (double click, retry after a network failure, another user) got there first: nothing was posted twice.
            db.ChangeTracker.Clear();
            var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
            var rack = CycleCountCounting.FindRack(batch, request.RackCode);
            if (rack.Status == CycleCountRackStatus.Submitted)
            {
                return await AlreadySubmittedAsync(batch, rack.RackCode, cancellationToken);
            }

            throw new InvalidOperationException("This rack was changed by another user while submitting. Please refresh and try again.");
        }
    }

    private async Task<CycleCountSubmitResultDto> AlreadySubmittedAsync(CycleCountBatch batch, string rackCode, CancellationToken cancellationToken)
    {
        var cards = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == batch.Id && c.RackCode == rackCode && !c.IsExcluded).Select(c => c.State).ToListAsync(cancellationToken);
        return new CycleCountSubmitResultDto(batch.BatchNumber, rackCode, true, cards.Count(s => s == CycleCountCardState.Verified),
            cards.Count(s => s == CycleCountCardState.Corrected), 0, batch.Status);
    }

    private async Task<CycleCountSubmitResultDto> RunAsync(SubmitCycleCountRackCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var rack = CycleCountCounting.FindRack(batch, request.RackCode);
        // Re-read both rows: a long-lived Blazor scope may hold a stale copy from before another submit.
        await db.Entry(rack).ReloadAsync(cancellationToken);
        await db.Entry(batch).ReloadAsync(cancellationToken);
        if (rack.Status == CycleCountRackStatus.Submitted)
        {
            return await AlreadySubmittedAsync(batch, rack.RackCode, cancellationToken);
        }

        if (rack.Status == CycleCountRackStatus.Excluded) throw new InvalidOperationException("This rack was excluded from the cycle count.");
        CycleCountCounting.EnsureCounting(batch);

        var cards = await db.CycleCountCards
            .Where(c => c.CycleCountBatchId == batch.Id && c.RackCode == rack.RackCode && !c.IsExcluded && c.SubmittedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var card in cards) await db.Entry(card).ReloadAsync(cancellationToken);
        if (cards.Count == 0) throw new InvalidOperationException("There are no counted cards to submit for this rack.");
        // Good stock must be counted in full. Damaged stock is optional: damage cards nobody counted are left out and the damaged stock stays as it is.
        var pending = cards.Count(c => c.State == CycleCountCardState.Pending && c.SystemStockSubtypeId != LocationSubtypeIds.Damage);
        if (pending > 0)
        {
            throw new InvalidOperationException($"{pending} card(s) of rack {rack.RackCode} are still pending. Count them all, or close the cycle count partially to leave them out.");
        }

        var now = clock.UtcNow;
        var uncountedDamage = cards.Where(c => c.State == CycleCountCardState.Pending).ToList();
        foreach (var card in uncountedDamage)
        {
            card.IsExcluded = true;
            card.RowVersion++;
        }

        cards = cards.Where(c => !c.IsExcluded).ToList();
        if (cards.Count == 0) throw new InvalidOperationException("There are no counted cards to submit for this rack.");
        var changes = await BuildChangesAsync(batch, cards, now, cancellationToken);
        if (changes.Count > 0)
        {
            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.CycleCount, "CYCLE_COUNT", batch.BatchNumber, $"Cycle count, rack {rack.RackCode}"),
                changes, cancellationToken);
        }

        foreach (var card in cards)
        {
            card.SubmittedAt = now;
            card.SubmittedByUserId = user.UserId;
            card.SubmittedByUserName = user.UserName;
            card.DifferenceBoxes = (card.PhysicalQuantityBoxes ?? 0) - card.SystemQuantityBoxes;
            card.RowVersion++;
        }

        rack.Status = CycleCountRackStatus.Submitted;
        rack.SubmittedAt = now;
        rack.SubmittedByUserId = user.UserId;
        rack.SubmittedByUserName = user.UserName;
        rack.RowVersion++;
        db.CycleCountReviews.Add(new CycleCountReview { CycleCountBatchId = batch.Id, RackCode = rack.RackCode, Status = CycleCountReviewStatus.PendingReview, CreatedAt = now, CreatedByUserId = user.UserId });
        if (batch.Racks.All(r => r.Status != CycleCountRackStatus.Open))
        {
            batch.Status = CycleCountBatchStatus.PendingReview;
        }

        CycleCountRules.Touch(batch, user, now);
        var verified = cards.Count(c => c.State == CycleCountCardState.Verified);
        var corrected = cards.Count(c => c.State == CycleCountCardState.Corrected);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.Submitted, user, now, null,
            $"{verified} verified, {corrected} corrected, {changes.Count} stock change(s) posted"
            + (uncountedDamage.Count > 0 ? $"; {uncountedDamage.Count} damage card(s) were not counted and the damaged stock was left unchanged" : string.Empty), rackCode: rack.RackCode);
        await db.SaveChangesAsync(cancellationToken);
        return new CycleCountSubmitResultDto(batch.BatchNumber, rack.RackCode, false, verified, corrected, changes.Count, batch.Status);
    }

    /// <summary>Per location: what the cards say is there now, against what the system holds right now. Only material + PKM cells that a card
    /// covers are touched; stock that appeared after the snapshot and was never seen by a counter is left alone.</summary>
    private async Task<List<StockChange>> BuildChangesAsync(CycleCountBatch batch, List<CycleCountCard> cards, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changes = new List<StockChange>();
        var positionIds = cards.Select(c => c.PalletPositionId).Distinct().ToList();
        var positions = await db.PalletPositions.Include(p => p.Rack).Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var live = await db.StockBatches.AsNoTracking()
            .Where(b => positionIds.Contains(b.PalletPositionId) && (b.StockSubtypeId == LocationSubtypeIds.Good || b.StockSubtypeId == LocationSubtypeIds.Damage))
            .Select(b => new { b.PalletPositionId, b.MaterialId, b.MfgMonth, b.StockSubtypeId, b.QuantityBoxes })
            .ToListAsync(cancellationToken);

        foreach (var group in cards.GroupBy(c => c.PalletPositionId))
        {
            var desired = new Dictionary<(int Material, int Pkm, int Subtype), decimal>();
            foreach (var card in group)
            {
                if (card.SystemMaterialId is { } sm && card.SystemMfgMonth is { } sp) desired.TryAdd((sm, sp, card.SystemStockSubtypeId), 0m);
                if (CycleCountCounting.EffectiveStock(card) is { } eff)
                {
                    desired[(eff.MaterialId, eff.Pkm, eff.SubtypeId)] = desired.GetValueOrDefault((eff.MaterialId, eff.Pkm, eff.SubtypeId)) + eff.Quantity;
                }
            }

            foreach (var (key, quantity) in desired)
            {
                var current = live.Where(l => l.PalletPositionId == group.Key && l.MaterialId == key.Material && l.MfgMonth == key.Pkm && l.StockSubtypeId == key.Subtype).Sum(l => l.QuantityBoxes);
                var snapshot = group.Where(c => c.SystemMaterialId == key.Material && c.SystemMfgMonth == key.Pkm && c.SystemStockSubtypeId == key.Subtype).Sum(c => c.SystemQuantityBoxes);
                if (snapshot != current && group.Any(c => c.SystemMaterialId == key.Material && c.SystemMfgMonth == key.Pkm && c.SystemStockSubtypeId == key.Subtype))
                {
                    // The stock moved between the snapshot and the submit (a pick, a put-away, a move). The count wins; say so.
                    CycleCountRules.AddHistory(db, batch, CycleCountActions.SnapshotDrift, user, now,
                        $"system {snapshot:0.###}", $"system now {current:0.###}, counted {quantity:0.###}",
                        $"{positions[group.Key].LocationCode} material/PKM {MfgMonthParser.Format(key.Pkm)}", group.First().RackCode, group.First().Id);
                }

                if (quantity != current)
                {
                    changes.Add(new StockChange(key.Material, key.Pkm, key.Subtype, positions[group.Key], quantity - current));
                }
            }
        }

        return changes;
    }
}
