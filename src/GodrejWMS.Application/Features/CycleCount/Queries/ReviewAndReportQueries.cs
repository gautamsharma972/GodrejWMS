using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Queries;

[RequiresPermission(PermissionKeys.CycleCount.Review)]
public sealed record GetCycleCountReviewsQuery(
    int? WarehouseId = null, CycleCountReviewStatus? Status = null, string? Search = null, int PageNumber = 1, int PageSize = 20) : IRequest<PaginatedList<CycleCountReviewListItemDto>>;

[RequiresPermission(PermissionKeys.CycleCount.Review, PermissionKeys.CycleCount.Reports)]
public sealed record GetCycleCountReviewDetailQuery(
    int BatchId, string RackCode, string? Location = null, long? MaterialNumber = null, string? CountStatus = null) : IRequest<CycleCountReviewDetailDto>;

[RequiresPermission(PermissionKeys.CycleCount.Reports)]
public sealed record GetCycleCountBatchSummaryReportQuery(CycleCountReportFilter Filter) : IRequest<IReadOnlyList<CycleCountBatchSummaryRowDto>>;

/// <summary>Location-wise report. With <paramref name="OnlyDiscrepancies"/> it is the Inventory Discrepancy report (only cards that differ from the system).</summary>
[RequiresPermission(PermissionKeys.CycleCount.Reports)]
public sealed record GetCycleCountLocationReportQuery(CycleCountReportFilter Filter, bool OnlyDiscrepancies = false) : IRequest<IReadOnlyList<CycleCountRowDto>>;

[RequiresPermission(PermissionKeys.CycleCount.Reports)]
public sealed record GetCycleCountUserReportQuery(CycleCountReportFilter Filter) : IRequest<IReadOnlyList<CycleCountUserRowDto>>;

internal static class CycleCountReporting
{
    public const int MaxRows = 20000;

    public static CycleCountRowKind KindOf(CycleCountCard c)
    {
        if (c.State != CycleCountCardState.Corrected) return CycleCountRowKind.Matched;
        if (c.IsExtra || c.SystemMaterialId is null) return CycleCountRowKind.Exceptional;
        // Counting zero leaves no physical material: that is a plain quantity discrepancy of the same stock, not a different material.
        var zeroCount = c.PhysicalMaterialId is null && (c.PhysicalQuantityBoxes ?? 0) == 0;
        var sameCell = zeroCount
            || (c.PhysicalMaterialId == c.SystemMaterialId && c.PhysicalMfgMonth == c.SystemMfgMonth && (c.PhysicalStockSubtypeId ?? c.SystemStockSubtypeId) == c.SystemStockSubtypeId);
        return sameCell ? CycleCountRowKind.Discrepancy : CycleCountRowKind.Exceptional;
    }

    public static string StatusText(CycleCountRowKind kind, CycleCountCard c) =>
        c.State == CycleCountCardState.Pending ? "Pending" : kind switch
        {
            CycleCountRowKind.Matched => "Matched",
            CycleCountRowKind.Discrepancy => "Discrepancy",
            _ => c.IsExtra || c.SystemMaterialId is null ? "New stock found" : "Material / PKM changed"
        };

    /// <summary>Cards (of batches the caller can see) that satisfy the report filter.</summary>
    public static IQueryable<CycleCountCard> Cards(IApplicationDbContext db, CycleCountReportFilter f, bool onlyCounted)
    {
        var q = db.CycleCountCards.AsNoTracking().Where(c => !c.IsExcluded);
        if (onlyCounted) q = q.Where(c => c.State != CycleCountCardState.Pending);
        if (f.WarehouseId is { } w) q = q.Where(c => c.CycleCountBatch.WarehouseId == w);
        if (f.From is { } from) q = q.Where(c => c.CycleCountBatch.ScheduleTo >= from);
        if (f.To is { } to) q = q.Where(c => c.CycleCountBatch.ScheduleFrom <= to);
        if (!string.IsNullOrWhiteSpace(f.BatchNumber)) { var b = f.BatchNumber.Trim(); q = q.Where(c => c.CycleCountBatch.BatchNumber.Contains(b)); }
        if (!string.IsNullOrWhiteSpace(f.RackCode)) { var r = f.RackCode.Trim().ToUpper(); q = q.Where(c => c.RackCode.ToUpper() == r); }
        if (!string.IsNullOrWhiteSpace(f.Location)) { var l = f.Location.Trim(); q = q.Where(c => c.PalletPosition.LocationCode.Contains(l)); }
        if (f.MaterialNumber is { } m) q = q.Where(c => (c.SystemMaterial != null && c.SystemMaterial.MaterialNumber == m) || (c.PhysicalMaterial != null && c.PhysicalMaterial.MaterialNumber == m));
        if (!string.IsNullOrWhiteSpace(f.CountedBy)) { var u = f.CountedBy.Trim(); q = q.Where(c => c.CountedByUserName != null && c.CountedByUserName.Contains(u)); }
        if (f.ReviewStatus is { } rs)
        {
            q = q.Where(c => db.CycleCountReviews.Any(v => v.CycleCountBatchId == c.CycleCountBatchId && v.RackCode == c.RackCode && v.Status == rs));
        }

        return q;
    }

    public static async Task<List<CycleCountRowDto>> RowsAsync(IApplicationDbContext db, IQueryable<CycleCountCard> query, string? countStatus, CancellationToken cancellationToken)
    {
        var cards = await query
            .Include(c => c.CycleCountBatch).ThenInclude(b => b.Warehouse)
            .Include(c => c.PalletPosition).Include(c => c.SystemMaterial).Include(c => c.PhysicalMaterial)
            .OrderBy(c => c.CycleCountBatch.BatchNumber).ThenBy(c => c.SequenceKey)
            .Take(MaxRows).ToListAsync(cancellationToken);
        var rows = cards.Select(c =>
        {
            var kind = KindOf(c);
            var shown = c.SystemMaterial ?? c.PhysicalMaterial;
            var pkm = c.SystemMfgMonth ?? c.PhysicalMfgMonth;
            var countedAs = kind == CycleCountRowKind.Exceptional && c.PhysicalMaterial is not null
                ? $"{c.PhysicalMaterial.MaterialNumber} {(c.PhysicalMfgMonth is { } pp ? MfgMonthParser.Format(pp) : "")}".Trim()
                : string.Empty;
            return new CycleCountRowDto(
                c.Id, c.CycleCountBatchId, c.CycleCountBatch.BatchNumber, c.CycleCountBatch.Warehouse.Name, c.RackCode, c.PalletPosition.LocationCode,
                shown?.MaterialNumber, shown?.Description, pkm is { } p ? MfgMonthParser.Format(p) : null,
                c.SystemQuantityBoxes, c.PhysicalQuantityBoxes, c.State == CycleCountCardState.Pending ? null : (c.PhysicalQuantityBoxes ?? 0) - c.SystemQuantityBoxes,
                (c.PhysicalStockSubtypeId ?? c.SystemStockSubtypeId) == LocationSubtypeIds.Damage ? "Damage" : "Good", countedAs, kind, StatusText(kind, c), c.CountedByUserName, c.CountedAt, c.SubmittedAt, c.IsExtra);
        });
        if (!string.IsNullOrWhiteSpace(countStatus))
        {
            rows = countStatus.Trim().ToLowerInvariant() switch
            {
                "matched" => rows.Where(r => r.Kind == CycleCountRowKind.Matched && r.CountStatus != "Pending"),
                "discrepancy" => rows.Where(r => r.Kind != CycleCountRowKind.Matched),
                "pending" => rows.Where(r => r.CountStatus == "Pending"),
                _ => rows
            };
        }

        return rows.ToList();
    }
}

public sealed class GetCycleCountReviewsHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountReviewsQuery, PaginatedList<CycleCountReviewListItemDto>>
{
    public async Task<PaginatedList<CycleCountReviewListItemDto>> Handle(GetCycleCountReviewsQuery request, CancellationToken cancellationToken)
    {
        if (request.WarehouseId is { } w && !await access.HasAccessAsync(w, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var query = db.CycleCountReviews.AsNoTracking().AsQueryable();
        if (request.WarehouseId is { } wid) query = query.Where(r => r.CycleCountBatch.WarehouseId == wid);
        if (request.Status is { } status) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(r => r.CycleCountBatch.BatchNumber.Contains(term) || r.RackCode.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, request.PageNumber);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var reviews = await query.Include(r => r.CycleCountBatch).ThenInclude(b => b.Warehouse)
            .OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        var items = new List<CycleCountReviewListItemDto>();
        foreach (var r in reviews) items.Add(await HeaderAsync(db, r, cancellationToken));
        return new PaginatedList<CycleCountReviewListItemDto>(items, total, page, size);
    }

    internal static async Task<CycleCountReviewListItemDto> HeaderAsync(IApplicationDbContext db, CycleCountReview r, CancellationToken cancellationToken)
    {
        var cards = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == r.CycleCountBatchId && c.RackCode == r.RackCode && !c.IsExcluded)
            .Select(c => new { c.State, c.DifferenceBoxes, c.SubmittedAt, c.SubmittedByUserName }).ToListAsync(cancellationToken);
        var corrected = cards.Where(c => c.State == CycleCountCardState.Corrected).ToList();
        var submitted = cards.Where(c => c.SubmittedAt != null).OrderByDescending(c => c.SubmittedAt).FirstOrDefault();
        return new CycleCountReviewListItemDto(
            r.CycleCountBatchId, r.CycleCountBatch.BatchNumber, r.CycleCountBatch.Warehouse?.Name ?? string.Empty, r.RackCode,
            submitted?.SubmittedByUserName, submitted?.SubmittedAt, cards.Count, cards.Count(c => c.State == CycleCountCardState.Verified), corrected.Count,
            corrected.Count, corrected.Sum(c => c.DifferenceBoxes ?? 0), r.Status, r.ReviewedByUserName, r.ReviewedAt, r.ReviewRemarks);
    }
}

public sealed class GetCycleCountReviewDetailHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountReviewDetailQuery, CycleCountReviewDetailDto>
{
    public async Task<CycleCountReviewDetailDto> Handle(GetCycleCountReviewDetailQuery request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var rack = CycleCountCounting.FindRack(batch, request.RackCode);
        var review = await db.CycleCountReviews.AsNoTracking().Include(r => r.CycleCountBatch).ThenInclude(b => b.Warehouse)
            .FirstOrDefaultAsync(r => r.CycleCountBatchId == batch.Id && r.RackCode == rack.RackCode, cancellationToken)
            ?? throw new InvalidOperationException("This rack has not been submitted yet, so there is nothing to review.");
        var filter = new CycleCountReportFilter(BatchNumber: batch.BatchNumber, RackCode: rack.RackCode, Location: request.Location, MaterialNumber: request.MaterialNumber);
        var rows = await CycleCountReporting.RowsAsync(db, CycleCountReporting.Cards(db, filter, onlyCounted: true).Where(c => c.CycleCountBatchId == batch.Id), request.CountStatus, cancellationToken);
        var history = await db.CycleCountHistory.AsNoTracking()
            .Where(h => h.CycleCountBatchId == batch.Id && (h.RackCode == rack.RackCode || h.RackCode == null))
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).Take(500)
            .Select(h => new CycleCountHistoryDto(h.Id, h.Action, h.RackCode, h.PreviousValue, h.NewValue, h.Reason, h.UserName, h.OccurredAt))
            .ToListAsync(cancellationToken);
        return new CycleCountReviewDetailDto(await GetCycleCountReviewsHandler.HeaderAsync(db, review, cancellationToken), rows, history);
    }
}

public sealed class GetCycleCountBatchSummaryReportHandler(IApplicationDbContext db)
    : IRequestHandler<GetCycleCountBatchSummaryReportQuery, IReadOnlyList<CycleCountBatchSummaryRowDto>>
{
    public async Task<IReadOnlyList<CycleCountBatchSummaryRowDto>> Handle(GetCycleCountBatchSummaryReportQuery request, CancellationToken cancellationToken)
    {
        var f = request.Filter;
        var q = db.CycleCountBatches.AsNoTracking().Include(b => b.Racks).Include(b => b.Warehouse).AsQueryable();
        if (f.WarehouseId is { } w) q = q.Where(b => b.WarehouseId == w);
        if (f.From is { } from) q = q.Where(b => b.ScheduleTo >= from);
        if (f.To is { } to) q = q.Where(b => b.ScheduleFrom <= to);
        if (!string.IsNullOrWhiteSpace(f.BatchNumber)) { var n = f.BatchNumber.Trim(); q = q.Where(b => b.BatchNumber.Contains(n)); }
        if (!string.IsNullOrWhiteSpace(f.RackCode)) { var r = f.RackCode.Trim().ToUpper(); q = q.Where(b => b.Racks.Any(x => x.RackCode.ToUpper() == r)); }
        if (f.ReviewStatus is { } rs) q = q.Where(b => db.CycleCountReviews.Any(v => v.CycleCountBatchId == b.Id && v.Status == rs));
        var batches = await q.OrderByDescending(b => b.CreatedAt).Take(CycleCountReporting.MaxRows).ToListAsync(cancellationToken);
        var ids = batches.Select(b => b.Id).ToList();
        var cards = await db.CycleCountCards.AsNoTracking().Where(c => ids.Contains(c.CycleCountBatchId))
            .Select(c => new { c.CycleCountBatchId, c.PalletPositionId, c.State, c.IsExcluded, c.SubmittedByUserName }).ToListAsync(cancellationToken);
        var reviews = await db.CycleCountReviews.AsNoTracking().Where(r => ids.Contains(r.CycleCountBatchId) && r.ReviewedByUserName != null)
            .Select(r => new { r.CycleCountBatchId, r.ReviewedByUserName }).ToListAsync(cancellationToken);
        return batches.Select(b =>
        {
            var mine = cards.Where(c => c.CycleCountBatchId == b.Id).ToList();
            var active = mine.Where(c => !c.IsExcluded).ToList();
            var done = active.Count(c => c.State != CycleCountCardState.Pending);
            return new CycleCountBatchSummaryRowDto(
                b.BatchNumber, b.Warehouse.Name, b.ScheduleFrom, b.ScheduleTo, b.ActualStartAt, b.ActualEndAt,
                string.Join(", ", b.Racks.OrderBy(r => r.RackCode, StringComparer.OrdinalIgnoreCase).Select(r => r.RackCode)),
                mine.Select(c => c.PalletPositionId).Distinct().Count(), active.Count, done, active.Count - done,
                active.Count(c => c.State == CycleCountCardState.Verified), active.Count(c => c.State == CycleCountCardState.Corrected),
                CycleCountRules.Percent(done, active.Count),
                string.Join(", ", mine.Where(c => c.SubmittedByUserName != null).Select(c => c.SubmittedByUserName!).Distinct()),
                string.Join(", ", reviews.Where(r => r.CycleCountBatchId == b.Id).Select(r => r.ReviewedByUserName!).Distinct()),
                b.Status);
        }).ToList();
    }
}

public sealed class GetCycleCountLocationReportHandler(IApplicationDbContext db)
    : IRequestHandler<GetCycleCountLocationReportQuery, IReadOnlyList<CycleCountRowDto>>
{
    public async Task<IReadOnlyList<CycleCountRowDto>> Handle(GetCycleCountLocationReportQuery request, CancellationToken cancellationToken)
    {
        var onlyCounted = request.OnlyDiscrepancies || string.IsNullOrWhiteSpace(request.Filter.CountStatus) || !request.Filter.CountStatus.Equals("pending", StringComparison.OrdinalIgnoreCase);
        var query = CycleCountReporting.Cards(db, request.Filter, onlyCounted);
        if (request.OnlyDiscrepancies) query = query.Where(c => c.State == CycleCountCardState.Corrected);
        return await CycleCountReporting.RowsAsync(db, query, request.OnlyDiscrepancies ? "discrepancy" : request.Filter.CountStatus, cancellationToken);
    }
}

public sealed class GetCycleCountUserReportHandler(IApplicationDbContext db)
    : IRequestHandler<GetCycleCountUserReportQuery, IReadOnlyList<CycleCountUserRowDto>>
{
    public async Task<IReadOnlyList<CycleCountUserRowDto>> Handle(GetCycleCountUserReportQuery request, CancellationToken cancellationToken)
    {
        // "Assigned" is the whole rack of the batch (counters share a rack): the user's completed share is measured against it.
        var all = await CycleCountReporting.Cards(db, request.Filter with { CountedBy = null, Location = null, MaterialNumber = null }, onlyCounted: false)
            .Include(c => c.CycleCountBatch).ThenInclude(b => b.Warehouse)
            .Select(c => new
            {
                c.CycleCountBatchId, Batch = c.CycleCountBatch.BatchNumber, Warehouse = c.CycleCountBatch.Warehouse.Name, c.RackCode,
                c.State, c.CountedByUserName, c.SubmittedAt
            }).Take(CycleCountReporting.MaxRows).ToListAsync(cancellationToken);
        var user = request.Filter.CountedBy?.Trim();
        return all.GroupBy(c => new { c.CycleCountBatchId, c.Batch, c.Warehouse, c.RackCode })
            .SelectMany(g =>
            {
                var total = g.Count();
                var pending = g.Count(c => c.State == CycleCountCardState.Pending);
                return g.Where(c => c.CountedByUserName != null).GroupBy(c => c.CountedByUserName!)
                    .Where(u => string.IsNullOrEmpty(user) || u.Key.Contains(user, StringComparison.OrdinalIgnoreCase))
                    .Select(u => new CycleCountUserRowDto(
                        u.Key, g.Key.Batch, g.Key.Warehouse, g.Key.RackCode, total, u.Count(), pending,
                        u.Count(c => c.State == CycleCountCardState.Corrected), u.Max(c => c.SubmittedAt), CycleCountRules.Percent(u.Count(), total)));
            })
            .OrderBy(r => r.UserName).ThenBy(r => r.BatchNumber).ThenBy(r => r.RackCode).ToList();
    }
}
