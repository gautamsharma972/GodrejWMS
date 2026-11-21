using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount;

/// <summary>Card generation, ordering, range selection and DTO mapping for the counting screens (web and mobile share these).</summary>
public static class CycleCountCounting
{
    public static readonly IReadOnlyList<CycleCountBatchStatus> CountingStatuses =
        [CycleCountBatchStatus.InProgress, CycleCountBatchStatus.Extended, CycleCountBatchStatus.PartiallyClosed];

    /// <summary>Cards can be created, counted and submitted only while the batch is being counted.</summary>
    public static void EnsureCounting(CycleCountBatch batch)
    {
        if (!CountingStatuses.Contains(batch.Status))
        {
            throw new InvalidOperationException(batch.Status switch
            {
                CycleCountBatchStatus.Scheduled => "This cycle count has not been started yet.",
                CycleCountBatchStatus.PendingReview => "Every rack of this cycle count is already submitted.",
                _ => $"This cycle count is {batch.Status} and cannot be counted."
            });
        }
    }

    public static CycleCountBatchRack FindRack(CycleCountBatch batch, string rackCode) =>
        batch.Racks.FirstOrDefault(r => string.Equals(r.RackCode, rackCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Rack {rackCode} is not part of cycle count {batch.BatchNumber}.");

    /// <summary>Snapshots the Good stock of every selected rack into count cards (one per location + material + PKM, plus one empty card for a
    /// location holding nothing). Runs once per batch.</summary>
    public static async Task GenerateCardsAsync(IApplicationDbContext db, CycleCountBatch batch, ICurrentUserService user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var codes = batch.Racks.Where(r => r.Status != CycleCountRackStatus.Excluded).Select(r => r.RackCode.ToUpperInvariant()).ToList();
        var positions = await db.PalletPositions.AsNoTracking()
            .Where(p => p.Rack.WarehouseId == batch.WarehouseId && p.IsActive && !p.IsVirtual && codes.Contains(p.Rack.Code.ToUpper()))
            .Select(p => new { p.Id, p.Column, p.Level, RackCode = p.Rack.Code })
            .ToListAsync(cancellationToken);
        var positionIds = positions.Select(p => p.Id).ToList();
        var stock = await db.StockBatches.AsNoTracking()
            .Where(b => positionIds.Contains(b.PalletPositionId) && b.StockSubtypeId == LocationSubtypeIds.Good && b.QuantityBoxes > 0)
            .Select(b => new { b.PalletPositionId, b.MaterialId, b.Material.MaterialNumber, b.MfgMonth, b.QuantityBoxes })
            .ToListAsync(cancellationToken);
        var byPosition = stock.ToLookup(s => s.PalletPositionId);

        foreach (var position in positions)
        {
            var rows = byPosition[position.Id].ToList();
            if (rows.Count == 0)
            {
                db.CycleCountCards.Add(new CycleCountCard
                {
                    CycleCountBatchId = batch.Id, RackCode = position.RackCode, PalletPositionId = position.Id,
                    SequenceKey = CycleCountRules.SequenceKey(position.RackCode, position.Column, position.Level, 0),
                    CreatedAt = now, CreatedByUserId = user.UserId, RowVersion = 1
                });
                continue;
            }

            foreach (var row in rows)
            {
                db.CycleCountCards.Add(new CycleCountCard
                {
                    CycleCountBatchId = batch.Id, RackCode = position.RackCode, PalletPositionId = position.Id,
                    SequenceKey = CycleCountRules.SequenceKey(position.RackCode, position.Column, position.Level, row.MaterialNumber),
                    SystemMaterialId = row.MaterialId, SystemMfgMonth = row.MfgMonth, SystemQuantityBoxes = row.QuantityBoxes,
                    CreatedAt = now, CreatedByUserId = user.UserId, RowVersion = 1
                });
            }
        }

        batch.CardsGenerated = true;

        // Damaged stock is on offer from the start: the counter picks Good, Damage or Both on the count screen.
        foreach (var rack in batch.Racks.Where(r => r.Status != CycleCountRackStatus.Excluded))
        {
            await GenerateDamageCardsAsync(db, batch, rack, user, now, cancellationToken);
        }
    }

    /// <summary>Adds count cards for the damaged stock of one rack. Once per rack (generation at start does it for every rack; batches started before that get it when reopened).</summary>
    public static async Task<int> GenerateDamageCardsAsync(IApplicationDbContext db, CycleCountBatch batch, CycleCountBatchRack rack, ICurrentUserService user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var positions = await db.PalletPositions.AsNoTracking()
            .Where(p => p.Rack.WarehouseId == batch.WarehouseId && p.IsActive && !p.IsVirtual && p.Rack.Code.ToUpper() == rack.RackCode.ToUpper())
            .Select(p => new { p.Id, p.Column, p.Level, RackCode = p.Rack.Code }).ToListAsync(cancellationToken);
        var ids = positions.Select(p => p.Id).ToList();
        var stock = await db.StockBatches.AsNoTracking()
            .Where(b => ids.Contains(b.PalletPositionId) && b.StockSubtypeId == LocationSubtypeIds.Damage && b.QuantityBoxes > 0)
            .Select(b => new { b.PalletPositionId, b.MaterialId, b.Material.MaterialNumber, b.MfgMonth, b.QuantityBoxes }).ToListAsync(cancellationToken);
        var added = 0;
        foreach (var row in stock)
        {
            var position = positions.First(p => p.Id == row.PalletPositionId);
            db.CycleCountCards.Add(new CycleCountCard
            {
                CycleCountBatchId = batch.Id, RackCode = position.RackCode, PalletPositionId = position.Id,
                SequenceKey = CycleCountRules.SequenceKey(position.RackCode, position.Column, position.Level, row.MaterialNumber, LocationSubtypeIds.Damage),
                SystemMaterialId = row.MaterialId, SystemMfgMonth = row.MfgMonth, SystemQuantityBoxes = row.QuantityBoxes, SystemStockSubtypeId = LocationSubtypeIds.Damage,
                CreatedAt = now, CreatedByUserId = user.UserId, RowVersion = 1
            });
            added++;
        }

        rack.IncludesDamageStock = true;
        rack.RowVersion++;
        return added;
    }

    /// <summary>The "rack|column|level" part of a sequence key: orders locations (not materials) the way the racks are laid out.</summary>
    public static string LocationOrder(string sequenceKey)
    {
        var parts = sequenceKey.Split('|');
        return parts.Length >= 3 ? string.Join('|', parts[0], parts[1], parts[2]) : sequenceKey;
    }

    public static async Task<CycleCountCardDto[]> MapAsync(IApplicationDbContext db, IReadOnlyList<CycleCountCard> cards, CancellationToken cancellationToken)
    {
        if (cards.Count == 0) return [];
        var positionIds = cards.Select(c => c.PalletPositionId).Distinct().ToList();
        var codes = await db.PalletPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.LocationCode, cancellationToken);
        var materialIds = cards.SelectMany(c => new[] { c.SystemMaterialId, c.PhysicalMaterialId }).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => new { m.MaterialNumber, m.Description }, cancellationToken);
        return cards.Select(c => new CycleCountCardDto(
            c.Id, c.RackCode, c.PalletPositionId, codes.GetValueOrDefault(c.PalletPositionId, string.Empty),
            c.SystemMaterialId, c.SystemMaterialId is { } sm ? materials[sm].MaterialNumber : null, c.SystemMaterialId is { } sd ? materials[sd].Description : null,
            c.SystemMfgMonth is { } spk ? MfgMonthParser.Format(spk) : null, c.SystemQuantityBoxes, c.SystemStockSubtypeId,
            c.PhysicalMaterialId, c.PhysicalMaterialId is { } pm ? materials[pm].MaterialNumber : null, c.PhysicalMaterialId is { } pd ? materials[pd].Description : null,
            c.PhysicalMfgMonth is { } ppk ? MfgMonthParser.Format(ppk) : null, c.PhysicalQuantityBoxes, c.PhysicalStockSubtypeId,
            c.DifferenceBoxes, c.State, c.IsExtra, c.IsExcluded, c.SubmittedAt != null,
            c.CountedByUserName, c.CountedAt, c.RowVersion)).ToArray();
    }

    /// <summary>Pending first, completed after; each group in location sequence. Never the database's retrieval order.</summary>
    public static IEnumerable<CycleCountCard> Sort(IEnumerable<CycleCountCard> cards) =>
        cards.OrderBy(c => c.State != CycleCountCardState.Pending).ThenBy(c => c.SequenceKey, StringComparer.Ordinal).ThenBy(c => c.Id);

    /// <summary>The material + PKM a card stands for right now (what it will leave in its location when submitted), if any.</summary>
    public static (int MaterialId, int Pkm, int SubtypeId, decimal Quantity)? EffectiveStock(CycleCountCard c)
    {
        if (c.State == CycleCountCardState.Corrected)
        {
            return c.PhysicalMaterialId is { } m && c.PhysicalMfgMonth is { } p && (c.PhysicalQuantityBoxes ?? 0) > 0
                ? (m, p, c.PhysicalStockSubtypeId ?? LocationSubtypeIds.Good, c.PhysicalQuantityBoxes!.Value) : null;
        }

        return c.SystemMaterialId is { } sm && c.SystemMfgMonth is { } sp ? (sm, sp, c.SystemStockSubtypeId, c.SystemQuantityBoxes) : null;
    }

    public static async Task<CycleCountCard> LoadCardAsync(IApplicationDbContext db, IWarehouseAccessService access, int cardId, CancellationToken cancellationToken)
    {
        var card = await db.CycleCountCards.Include(c => c.CycleCountBatch).ThenInclude(b => b.Racks)
            .FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken)
            ?? throw new NotFoundException(nameof(CycleCountCard), cardId);
        if (!await access.HasAccessAsync(card.CycleCountBatch.WarehouseId, cancellationToken))
        {
            throw new NotFoundException(nameof(CycleCountCard), cardId);
        }

        // Same reason as LoadBatchAsync: never write on a copy this circuit loaded earlier.
        await db.Entry(card).ReloadAsync(cancellationToken);
        await db.Entry(card.CycleCountBatch).ReloadAsync(cancellationToken);
        foreach (var rack in card.CycleCountBatch.Racks.ToList())
        {
            await db.Entry(rack).ReloadAsync(cancellationToken);
        }

        return card;
    }

    public static void EnsureEditable(CycleCountCard card)
    {
        EnsureCounting(card.CycleCountBatch);
        if (card.SubmittedAt is not null || FindRack(card.CycleCountBatch, card.RackCode).Status == CycleCountRackStatus.Submitted)
        {
            throw new InvalidOperationException("This rack has been submitted; its records can no longer be changed.");
        }

        if (card.IsExcluded)
        {
            throw new InvalidOperationException("This card was excluded when the cycle count was closed partially.");
        }
    }

    public static string Describe(CycleCountCard c, IReadOnlyDictionary<int, long> numbers) => c.State switch
    {
        CycleCountCardState.Pending => "pending",
        CycleCountCardState.Verified => $"OK {c.PhysicalQuantityBoxes:0.###}",
        _ => c.PhysicalQuantityBoxes is null or 0
            ? "0 boxes"
            : $"{(c.PhysicalMaterialId is { } m && numbers.TryGetValue(m, out var n) ? n.ToString() : "?")} {(c.PhysicalMfgMonth is { } p ? MfgMonthParser.Format(p) : "?")} x {c.PhysicalQuantityBoxes:0.###}{((c.PhysicalStockSubtypeId ?? LocationSubtypeIds.Good) == LocationSubtypeIds.Damage ? " (damage)" : "")}"
    };
}
