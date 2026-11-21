using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.CycleCount.Commands;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Queries;

[RequiresPermission(PermissionKeys.CycleCount.Schedule, PermissionKeys.CycleCount.ManageSchedule)]
public sealed record GetCycleCountRackOptionsQuery(int WarehouseId) : IRequest<IReadOnlyList<CycleCountRackOptionDto>>;

[RequiresPermission(PermissionKeys.CycleCount.Schedule)]
public sealed record GetCycleCountScheduleWarningsQuery(int WarehouseId) : IRequest<CycleCountScheduleWarningsDto>;

[RequiresPermission(PermissionKeys.Menu.ViewCycleCount)]
public sealed record GetCycleCountBatchesQuery(
    int? WarehouseId = null, CycleCountBatchStatus? Status = null, string? Search = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    int PageNumber = 1, int PageSize = 20) : IRequest<PaginatedList<CycleCountBatchDto>>;

[RequiresPermission(PermissionKeys.Menu.ViewCycleCount)]
public sealed record GetCycleCountBatchDetailQuery(int BatchId) : IRequest<CycleCountBatchDetailDto>;

/// <summary>Whether Inward / Pullout creation is currently blocked for a warehouse (used for the banners; the real block is in the handlers).</summary>
public sealed record GetCycleCountFreezeQuery(int WarehouseId) : IRequest<CycleCountFreezeDto>;

public sealed class GetCycleCountRackOptionsHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountRackOptionsQuery, IReadOnlyList<CycleCountRackOptionDto>>
{
    public async Task<IReadOnlyList<CycleCountRackOptionDto>> Handle(GetCycleCountRackOptionsQuery request, CancellationToken cancellationToken)
    {
        if (!await access.HasAccessAsync(request.WarehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var rows = await db.PalletPositions.AsNoTracking()
            .Where(p => p.Rack.WarehouseId == request.WarehouseId && p.IsActive && !p.IsVirtual)
            .GroupBy(p => p.Rack.Code)
            .Select(g => new { RackCode = g.Key, Locations = g.Count(), Name = g.Select(p => p.Rack.Name).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.RackCode, StringComparer.OrdinalIgnoreCase)
            .Select(r => new CycleCountRackOptionDto(r.RackCode, r.Locations, string.IsNullOrWhiteSpace(r.Name) ? r.RackCode : r.Name!)).ToList();
    }
}

public sealed class GetCycleCountScheduleWarningsHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountScheduleWarningsQuery, CycleCountScheduleWarningsDto>
{
    public async Task<CycleCountScheduleWarningsDto> Handle(GetCycleCountScheduleWarningsQuery request, CancellationToken cancellationToken)
    {
        if (!await access.HasAccessAsync(request.WarehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        return await CycleCountWarnings.BuildAsync(db, request.WarehouseId, cancellationToken);
    }
}

public sealed class GetCycleCountBatchesHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountBatchesQuery, PaginatedList<CycleCountBatchDto>>
{
    public async Task<PaginatedList<CycleCountBatchDto>> Handle(GetCycleCountBatchesQuery request, CancellationToken cancellationToken)
    {
        if (request.WarehouseId is { } w && !await access.HasAccessAsync(w, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var query = db.CycleCountBatches.AsNoTracking().Include(b => b.Racks).AsQueryable();
        if (request.WarehouseId is { } wid) query = query.Where(b => b.WarehouseId == wid);
        if (request.Status is { } status) query = query.Where(b => b.Status == status);
        if (request.From is { } from) query = query.Where(b => b.ScheduleTo >= from);
        if (request.To is { } to) query = query.Where(b => b.ScheduleFrom <= to);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(b => b.BatchNumber.Contains(term) || b.Racks.Any(r => r.RackCode.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = Math.Max(1, request.PageNumber);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var items = await query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PaginatedList<CycleCountBatchDto>(await CycleCountRules.ToDtosAsync(db, items, cancellationToken), total, page, size);
    }
}

public sealed class GetCycleCountBatchDetailHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountBatchDetailQuery, CycleCountBatchDetailDto>
{
    public async Task<CycleCountBatchDetailDto> Handle(GetCycleCountBatchDetailQuery request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var history = await db.CycleCountHistory.AsNoTracking()
            .Where(h => h.CycleCountBatchId == batch.Id)
            .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id)
            .Select(h => new CycleCountHistoryDto(h.Id, h.Action, h.RackCode, h.PreviousValue, h.NewValue, h.Reason, h.UserName, h.OccurredAt))
            .ToListAsync(cancellationToken);
        return new CycleCountBatchDetailDto((await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0], history);
    }
}

public sealed class GetCycleCountFreezeHandler(IApplicationDbContext db, IDateTimeProvider clock, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountFreezeQuery, CycleCountFreezeDto>
{
    public async Task<CycleCountFreezeDto> Handle(GetCycleCountFreezeQuery request, CancellationToken cancellationToken)
    {
        if (!await access.HasAccessAsync(request.WarehouseId, cancellationToken))
        {
            return new CycleCountFreezeDto(false, null, null);
        }

        var batch = await CycleCountFreeze.FindFreezingBatchAsync(db, request.WarehouseId, clock.UtcNow, cancellationToken);
        return batch is null
            ? new CycleCountFreezeDto(false, null, null)
            : new CycleCountFreezeDto(true, batch.BatchNumber, CycleCountFreeze.Message(batch));
    }
}

[RequiresPermission(PermissionKeys.CycleCount.Perform, PermissionKeys.CycleCount.Submit)]
public sealed record GetMyCycleCountBatchesQuery() : IRequest<IReadOnlyList<CycleCountBatchDto>>;

[RequiresPermission(PermissionKeys.CycleCount.Perform, PermissionKeys.CycleCount.Submit)]
public sealed record GetCycleCountLocationsQuery(int BatchId, string RackCode) : IRequest<IReadOnlyList<CycleCountLocationDto>>;

/// <summary>The count cards of one rack, optionally limited to a From / To location range (inclusive, in rack sequence), already in display order.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Perform, PermissionKeys.CycleCount.Submit, PermissionKeys.CycleCount.Review, PermissionKeys.CycleCount.Reports)]
public sealed record GetCycleCountCardsQuery(int BatchId, string RackCode, string? FromLocation = null, string? ToLocation = null, int? StockSubtypeId = null) : IRequest<CycleCountCardsDto>;

[RequiresPermission(PermissionKeys.CycleCount.Perform, PermissionKeys.CycleCount.Submit)]
public sealed record GetCycleCountSubmitSummaryQuery(int BatchId, string RackCode) : IRequest<CycleCountSubmitSummaryDto>;

public sealed class GetMyCycleCountBatchesHandler(IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock)
    : IRequestHandler<GetMyCycleCountBatchesQuery, IReadOnlyList<CycleCountBatchDto>>
{
    public async Task<IReadOnlyList<CycleCountBatchDto>> Handle(GetMyCycleCountBatchesQuery request, CancellationToken cancellationToken)
    {
        var warehouseIds = (await access.GetAccessibleWarehousesAsync(cancellationToken)).Select(w => w.Id).ToList();
        var now = clock.UtcNow;
        var batches = await db.CycleCountBatches.AsNoTracking().Include(b => b.Racks)
            .Where(b => warehouseIds.Contains(b.WarehouseId) && b.ScheduleFrom <= now
                && (b.Status == CycleCountBatchStatus.Scheduled || b.Status == CycleCountBatchStatus.InProgress
                    || b.Status == CycleCountBatchStatus.Extended || b.Status == CycleCountBatchStatus.PartiallyClosed))
            .OrderBy(b => b.ScheduleFrom).ToListAsync(cancellationToken);
        return await CycleCountRules.ToDtosAsync(db, batches, cancellationToken);
    }
}

public sealed class GetCycleCountLocationsHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountLocationsQuery, IReadOnlyList<CycleCountLocationDto>>
{
    public async Task<IReadOnlyList<CycleCountLocationDto>> Handle(GetCycleCountLocationsQuery request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var rack = CycleCountCounting.FindRack(batch, request.RackCode);
        var cards = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == batch.Id && c.RackCode == rack.RackCode && !c.IsExcluded)
            .Select(c => new { c.PalletPositionId, c.PalletPosition.LocationCode, c.SequenceKey, c.State })
            .ToListAsync(cancellationToken);
        return cards.GroupBy(c => new { c.PalletPositionId, c.LocationCode, Order = CycleCountCounting.LocationOrder(c.SequenceKey) })
            .OrderBy(g => g.Key.Order, StringComparer.Ordinal)
            .Select(g => new CycleCountLocationDto(g.Key.PalletPositionId, g.Key.LocationCode, g.Count(), g.Count(c => c.State == CycleCountCardState.Pending)))
            .ToList();
    }
}

public sealed class GetCycleCountCardsHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountCardsQuery, CycleCountCardsDto>
{
    public async Task<CycleCountCardsDto> Handle(GetCycleCountCardsQuery request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var rack = CycleCountCounting.FindRack(batch, request.RackCode);
        var all = await db.CycleCountCards.AsNoTracking()
            .Include(c => c.PalletPosition)
            .Where(c => c.CycleCountBatchId == batch.Id && c.RackCode == rack.RackCode && !c.IsExcluded)
            .ToListAsync(cancellationToken);

        // Stock type filter (the screens default to Good); the damage cards only exist once the counters switched damage counting on.
        if (request.StockSubtypeId is { } subtypeFilter) all = all.Where(c => (c.PhysicalStockSubtypeId ?? c.SystemStockSubtypeId) == subtypeFilter || c.SystemStockSubtypeId == subtypeFilter).ToList();
        var selected = all;
        if (!string.IsNullOrWhiteSpace(request.FromLocation) || !string.IsNullOrWhiteSpace(request.ToLocation))
        {
            string Order(string code)
            {
                var card = all.FirstOrDefault(c => string.Equals(c.PalletPosition.LocationCode, code.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Location {code} does not belong to rack {rack.RackCode} of this cycle count.");
                return CycleCountCounting.LocationOrder(card.SequenceKey);
            }

            var first = all.Select(c => CycleCountCounting.LocationOrder(c.SequenceKey)).Min(StringComparer.Ordinal)!;
            var last = all.Select(c => CycleCountCounting.LocationOrder(c.SequenceKey)).Max(StringComparer.Ordinal)!;
            var from = string.IsNullOrWhiteSpace(request.FromLocation) ? first : Order(request.FromLocation);
            var to = string.IsNullOrWhiteSpace(request.ToLocation) ? last : Order(request.ToLocation);
            if (string.CompareOrdinal(from, to) > 0)
            {
                throw new InvalidOperationException("From Location cannot be after To Location.");
            }

            selected = all.Where(c =>
            {
                var o = CycleCountCounting.LocationOrder(c.SequenceKey);
                return string.CompareOrdinal(o, from) >= 0 && string.CompareOrdinal(o, to) <= 0;
            }).ToList();
        }

        var sorted = CycleCountCounting.Sort(selected).ToList();
        var done = sorted.Count(c => c.State != CycleCountCardState.Pending);
        return new CycleCountCardsDto(batch.Id, batch.BatchNumber, rack.RackCode, batch.Status, rack.Status,
            await CycleCountCounting.MapAsync(db, sorted, cancellationToken), rack.IncludesDamageStock, sorted.Count, done, sorted.Count - done, CycleCountRules.Percent(done, sorted.Count));
    }
}

public sealed class GetCycleCountSubmitSummaryHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountSubmitSummaryQuery, CycleCountSubmitSummaryDto>
{
    public async Task<CycleCountSubmitSummaryDto> Handle(GetCycleCountSubmitSummaryQuery request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        var rack = CycleCountCounting.FindRack(batch, request.RackCode);
        var cards = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == batch.Id && c.RackCode == rack.RackCode).ToListAsync(cancellationToken);
        var active = cards.Where(c => !c.IsExcluded).ToList();
        var pending = active.Count(c => c.State == CycleCountCardState.Pending && c.SystemStockSubtypeId != LocationSubtypeIds.Damage);
        var uncountedDamage = active.Count(c => c.State == CycleCountCardState.Pending && c.SystemStockSubtypeId == LocationSubtypeIds.Damage);
        var diff = active.Where(c => c.State == CycleCountCardState.Corrected).Select(c => c.DifferenceBoxes ?? ((c.PhysicalQuantityBoxes ?? 0) - c.SystemQuantityBoxes)).ToList();
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == batch.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        string? blocker = rack.Status switch
        {
            CycleCountRackStatus.Submitted => "This rack is already submitted.",
            CycleCountRackStatus.Excluded => "This rack was excluded from the cycle count.",
            _ when !CycleCountCounting.CountingStatuses.Contains(batch.Status) => $"This cycle count is {batch.Status}.",
            _ when active.Count == 0 => "There are no cards to submit.",
            _ when active.Count == uncountedDamage => "Only damaged stock is left and none of it is counted yet.",
            _ when pending > 0 => $"{pending} card(s) are still pending.",
            _ => null
        };
        return new CycleCountSubmitSummaryDto(batch.Id, batch.BatchNumber, warehouse, rack.RackCode, rack.Status, active.Count,
            active.Count(c => c.State == CycleCountCardState.Verified), active.Count(c => c.State == CycleCountCardState.Corrected), pending,
            cards.Count(c => c.IsExcluded), diff.Sum(), diff.Sum(Math.Abs), blocker is null, blocker, uncountedDamage);
    }
}
