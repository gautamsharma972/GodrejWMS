using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount;

/// <summary>Shared rules of the Cycle Count module: numbering, loading a batch the caller may use, history, progress, and the
/// inward / pullout freeze.</summary>
public static class CycleCountRules
{
    public static readonly IReadOnlyList<CycleCountBatchStatus> OpenStatuses =
    [
        CycleCountBatchStatus.Scheduled, CycleCountBatchStatus.InProgress, CycleCountBatchStatus.Extended,
        CycleCountBatchStatus.PartiallyClosed, CycleCountBatchStatus.PendingReview
    ];

    /// <summary>Date + sequence number (CC-yyyyMMdd-000001). Counted over every warehouse: the number is unique across warehouses.</summary>
    public static async Task<string> NextBatchNumberAsync(IApplicationDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = $"CC-{now:yyyyMMdd}-";
        var numbers = await db.CycleCountBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.BatchNumber.StartsWith(prefix)).Select(b => b.BatchNumber).ToListAsync(cancellationToken);
        var next = numbers.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:D6}";
    }

    /// <summary>The numeric-safe ordering key shared by web and mobile: rack code, column, level, then material.</summary>
    public static string SequenceKey(string rackCode, int column, int level, long materialNumber, int subtypeId = LocationSubtypeIds.Good) =>
        $"{rackCode.ToUpperInvariant()}|{column:D4}|{level:D3}|{materialNumber:D12}|{subtypeId}";

    public static async Task<CycleCountBatch> LoadBatchAsync(
        IApplicationDbContext db, IWarehouseAccessService access, int batchId, CancellationToken cancellationToken)
    {
        // The warehouse query filter already hides other warehouses' batches; the explicit check keeps writes safe even if a filter is ever bypassed.
        var batch = await db.CycleCountBatches.Include(b => b.Racks)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new NotFoundException(nameof(CycleCountBatch), batchId);
        if (!await access.HasAccessAsync(batch.WarehouseId, cancellationToken))
        {
            throw new NotFoundException(nameof(CycleCountBatch), batchId);
        }

        // A Blazor circuit keeps one long-lived DbContext: a batch it loaded earlier is handed back as it was then, even though counters on other
        // devices have changed it since (their count, a started batch, damage counting ...). Writing on that stale copy fails with a concurrency
        // error, so read the current row (and its racks) again before anything is changed.
        await db.Entry(batch).ReloadAsync(cancellationToken);
        foreach (var rack in batch.Racks.ToList())
        {
            await db.Entry(rack).ReloadAsync(cancellationToken);
        }

        return batch;
    }

    public static void AddHistory(
        IApplicationDbContext db, CycleCountBatch batch, string action, ICurrentUserService user, DateTimeOffset now,
        string? previous = null, string? updated = null, string? reason = null, string? rackCode = null, int? cardId = null)
    {
        db.CycleCountHistory.Add(new CycleCountHistory
        {
            CycleCountBatch = batch,
            CycleCountCardId = cardId,
            RackCode = rackCode,
            Action = action,
            PreviousValue = Truncate(previous),
            NewValue = Truncate(updated),
            Reason = reason is null ? null : Truncate(reason, 500),
            UserId = user.UserId,
            UserName = user.UserName,
            OccurredAt = now
        });
    }

    private static string? Truncate(string? value, int max = 1000) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    public static void Touch(CycleCountBatch batch, ICurrentUserService user, DateTimeOffset now)
    {
        batch.UpdatedAt = now;
        batch.UpdatedByUserId = user.UserId;
        batch.UpdatedByUserName = user.UserName;
        batch.RowVersion++;
    }

    /// <summary>A card counts as completed once the counter confirmed or corrected it (submitted or not); excluded cards are not part of the work.</summary>
    public static bool IsCompleted(CycleCountCard c) => !c.IsExcluded && c.State != CycleCountCardState.Pending;

    public static decimal Percent(int completed, int total) => total == 0 ? 0m : Math.Round(completed * 100m / total, 1);

    public static async Task<IReadOnlyList<CycleCountBatchDto>> ToDtosAsync(
        IApplicationDbContext db, IReadOnlyList<CycleCountBatch> batches, CancellationToken cancellationToken)
    {
        if (batches.Count == 0)
        {
            return [];
        }

        var ids = batches.Select(b => b.Id).ToList();
        var cards = await db.CycleCountCards.AsNoTracking()
            .Where(c => ids.Contains(c.CycleCountBatchId))
            .Select(c => new { c.CycleCountBatchId, c.RackCode, c.PalletPositionId, c.State, c.IsExcluded, Submitted = c.SubmittedAt != null, c.SequenceKey, LocationCode = c.PalletPosition.LocationCode })
            .ToListAsync(cancellationToken);
        var reviews = await db.CycleCountReviews.AsNoTracking()
            .Where(r => ids.Contains(r.CycleCountBatchId) && r.Status == CycleCountReviewStatus.PendingReview)
            .Select(r => r.CycleCountBatchId).ToListAsync(cancellationToken);
        var warehouseIds = batches.Select(b => b.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        // Rack names (the code is the key; the name is what people recognise on the floor).
        var rackNames = (await db.Racks.AsNoTracking().Where(r => warehouseIds.Contains(r.WarehouseId)).Select(r => new { r.WarehouseId, r.Code, r.Name }).ToListAsync(cancellationToken))
            .GroupBy(r => (r.WarehouseId, Code: r.Code.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(r => string.IsNullOrWhiteSpace(r.Name) ? r.Code : r.Name!).Distinct()));

        // Before the cards exist, the location count comes from the racks themselves.
        var notGenerated = batches.Where(b => !b.CardsGenerated).ToList();
        var locationsByRack = new Dictionary<(int WarehouseId, string RackCode), int>();
        if (notGenerated.Count > 0)
        {
            var wh = notGenerated.Select(b => b.WarehouseId).Distinct().ToList();
            var positions = await db.PalletPositions.AsNoTracking()
                .Where(p => wh.Contains(p.Rack.WarehouseId) && p.IsActive && !p.IsVirtual)
                .GroupBy(p => new { p.Rack.WarehouseId, p.Rack.Code })
                .Select(g => new { g.Key.WarehouseId, g.Key.Code, Count = g.Count() })
                .ToListAsync(cancellationToken);
            foreach (var p in positions) locationsByRack[(p.WarehouseId, p.Code.ToUpperInvariant())] = p.Count;
        }

        return batches.Select(b =>
        {
            var mine = cards.Where(c => c.CycleCountBatchId == b.Id).ToList();
            var racks = b.Racks.OrderBy(r => r.RackCode, StringComparer.OrdinalIgnoreCase).Select(r =>
            {
                var rc = mine.Where(c => string.Equals(c.RackCode, r.RackCode, StringComparison.OrdinalIgnoreCase) && !c.IsExcluded).ToList();
                var done = rc.Count(c => c.State != CycleCountCardState.Pending);
                var ordered = mine.Where(c => string.Equals(c.RackCode, r.RackCode, StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.SequenceKey, StringComparer.Ordinal).ToList();
                return new CycleCountRackDto(r.RackCode, r.Status, rc.Count, done, rc.Count - done, r.SubmittedByUserName, r.SubmittedAt, r.IncludesDamageStock,
                    rackNames.GetValueOrDefault((b.WarehouseId, r.RackCode.ToUpperInvariant()), r.RackCode), ordered.FirstOrDefault()?.LocationCode, ordered.LastOrDefault()?.LocationCode);
            }).ToList();
            var active = mine.Where(c => !c.IsExcluded).ToList();
            var completed = active.Count(c => c.State != CycleCountCardState.Pending);
            var locations = b.CardsGenerated
                ? mine.Select(c => c.PalletPositionId).Distinct().Count()
                : b.Racks.Sum(r => locationsByRack.GetValueOrDefault((b.WarehouseId, r.RackCode.ToUpperInvariant())));
            return new CycleCountBatchDto(
                b.Id, b.BatchNumber, b.WarehouseId, warehouses.GetValueOrDefault(b.WarehouseId, string.Empty),
                b.ScheduleFrom, b.ScheduleTo, b.ActualStartAt, b.ActualEndAt, b.Status, b.Remarks, b.DropReason, b.CardsGenerated, b.AutoReleaseFreezeAtScheduleEnd,
                racks, locations, active.Count, completed, active.Count - completed,
                active.Count(c => c.Submitted), mine.Count(c => c.IsExcluded), reviews.Count(r => r == b.Id),
                Percent(completed, active.Count), b.CreatedByUserName, b.CreatedAt);
        }).ToList();
    }
}

/// <summary>While a cycle count is open for a warehouse, nothing new may be created for it in Inward or Pullout: stock must stand still while it is counted.</summary>
public static class CycleCountFreeze
{
    public static async Task<CycleCountBatch?> FindFreezingBatchAsync(
        IApplicationDbContext db, int warehouseId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Every warehouse's batches are looked at: the guard must not depend on what the caller's own scope can see.
        return await db.CycleCountBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId
                && (b.Status == CycleCountBatchStatus.Scheduled || b.Status == CycleCountBatchStatus.InProgress
                    || b.Status == CycleCountBatchStatus.Extended || b.Status == CycleCountBatchStatus.PartiallyClosed
                    || b.Status == CycleCountBatchStatus.PendingReview)
                && b.ScheduleFrom <= now
                && (!b.AutoReleaseFreezeAtScheduleEnd || b.ScheduleTo > now))
            .OrderBy(b => b.ScheduleFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static string Message(CycleCountBatch batch) =>
        $"A cycle count ({batch.BatchNumber}) is in progress for this warehouse, so new Inward and Pullout requests cannot be created or uploaded right now. "
        + (batch.AutoReleaseFreezeAtScheduleEnd
            ? $"They are allowed again after {batch.ScheduleTo.ToLocalTime():dd MMM yyyy HH:mm}, or as soon as the cycle count is closed or dropped. "
            : "They are allowed again once the cycle count is closed or dropped. ")
        + "Please wait, or ask the Cycle Count scheduler / supervisor.";

    /// <summary>Throws when the warehouse is frozen. Called at the top of every handler that creates an Inward or Pullout order.</summary>
    public static async Task EnsureNotFrozenAsync(
        IApplicationDbContext db, IDateTimeProvider clock, int warehouseId, CancellationToken cancellationToken)
    {
        var batch = await FindFreezingBatchAsync(db, warehouseId, clock.UtcNow, cancellationToken);
        if (batch is not null)
        {
            throw new InvalidOperationException(Message(batch));
        }
    }
}
