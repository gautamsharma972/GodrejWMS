using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>Virtual (overflow) locations. A virtual location is an ordinary one-pallet <see cref="PalletPosition"/> in a hidden
/// per-warehouse system rack, so every capacity / stock / picking rule that works for physical bins works for it unchanged.
/// On MySQL the FREE one is taken with <c>FOR UPDATE SKIP LOCKED</c> and new codes come from a row-locked counter, so
/// concurrent uploads can neither share a location nor produce the same code.</summary>
public sealed class VirtualLocationService(AppDbContext db, IDateTimeProvider clock) : IVirtualLocationService
{
    private const int MaxCodeWarehouseLength = 10; // LocationCode is limited to 20: "VL-" + warehouse + "-" + 6 digits

    public async Task<PalletPosition> AcquireAsync(int warehouseId, int? inwardTransactionId, CancellationToken cancellationToken = default)
    {
        PalletPosition? position = null;
        var freeId = await FindFreeAsync(warehouseId, cancellationToken);
        if (freeId is { } id)
        {
            // The long-lived Blazor context may hold a stale copy of this row.
            var tracked = db.ChangeTracker.Entries<PalletPosition>().FirstOrDefault(e => e.Entity.Id == id);
            if (tracked is not null)
            {
                await tracked.ReloadAsync(cancellationToken);
            }

            position = await db.PalletPositions.Include(p => p.Rack).FirstAsync(p => p.Id == id, cancellationToken);
        }

        position ??= await CreateAsync(warehouseId, cancellationToken);
        position.VirtualStatus = VirtualLocationStatus.InUse;
        position.RowVersion++;
        db.VirtualLocationLogs.Add(new VirtualLocationLog
        {
            PalletPosition = position,
            InwardTransactionId = inwardTransactionId,
            AssignedAt = clock.UtcNow
        });

        // Saved at once: a second pallet of the same upload must not be handed the same row again.
        await db.SaveChangesAsync(cancellationToken);
        return position;
    }

    public async Task<bool> ReleaseIfEmptyAsync(PalletPosition position, CancellationToken cancellationToken = default)
    {
        if (!position.IsVirtual || position.VirtualStatus != VirtualLocationStatus.InUse || position.Id == 0)
        {
            return false;
        }

        // On-hand: batches pending in the change tracker (their current values) plus the rest as stored.
        var tracked = db.ChangeTracker.Entries<StockBatch>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.PalletPositionId == position.Id)
            .Select(e => e.Entity)
            .ToList();
        var trackedIds = tracked.Select(b => b.Id).Where(id => id != 0).ToList();
        var stored = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == position.Id && !trackedIds.Contains(b.Id))
            .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        if (tracked.Sum(b => b.QuantityBoxes) + stored > 0)
        {
            return false;
        }

        // Still reserved by an open (unposted) putaway or a pick of an order that is not closed.
        var hasOpenPutaway = await db.InwardPutaways.AsNoTracking()
            .AnyAsync(p => p.PalletPositionId == position.Id && !p.IsConfirmed
                && !p.InwardTransactionLine.InwardTransaction.IsRejected, cancellationToken);
        if (hasOpenPutaway)
        {
            return false;
        }

        // A pick the picker has not confirmed yet still means boxes physically waiting at this location, whether the order was
        // posted on upload or is an older pending one. Picks changed in this unit of work count with their new values.
        var trackedPicks = db.ChangeTracker.Entries<PulloutPick>()
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity)
            .Where(p => p.PalletPositionId == position.Id || ReferenceEquals(p.PalletPosition, position))
            .ToList();
        if (trackedPicks.Any(p => p.PickedAt == null && p.PulloutTransactionLine?.PulloutTransaction?.IsRejected != true))
        {
            return false;
        }

        var trackedPickIds = db.ChangeTracker.Entries<PulloutPick>().Select(e => e.Entity.Id).Where(id => id != 0).ToList();
        var hasOpenPick = await db.PulloutPicks.AsNoTracking()
            .AnyAsync(p => p.PalletPositionId == position.Id && p.PickedAt == null && !trackedPickIds.Contains(p.Id)
                && !p.PulloutTransactionLine.PulloutTransaction.IsRejected, cancellationToken);
        if (hasOpenPick)
        {
            return false;
        }

        var now = clock.UtcNow;
        position.VirtualStatus = VirtualLocationStatus.Free;
        position.LastReleasedAt = now;
        position.RowVersion++;
        var openLogs = await db.VirtualLocationLogs
            .Where(l => l.PalletPositionId == position.Id && l.ReleasedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var log in openLogs)
        {
            log.ReleasedAt = now;
        }

        return true;
    }

    private async Task<int?> FindFreeAsync(int warehouseId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var ids = await db.Database.SqlQuery<int>($@"
                SELECT p.Id AS Value
                FROM PalletPositions p
                INNER JOIN Racks r ON r.Id = p.RackId
                WHERE p.IsVirtual = 1 AND p.VirtualStatus = 'FREE' AND p.IsActive = 1 AND r.WarehouseId = {warehouseId}
                ORDER BY p.`Column`
                LIMIT 1
                FOR UPDATE OF p SKIP LOCKED").ToListAsync(cancellationToken);
            return ids.Count == 0 ? null : ids[0];
        }

        return await db.PalletPositions
            .Where(p => p.IsVirtual && p.VirtualStatus == VirtualLocationStatus.Free && p.IsActive && p.Rack.WarehouseId == warehouseId)
            .OrderBy(p => p.Column)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PalletPosition> CreateAsync(int warehouseId, CancellationToken cancellationToken)
    {
        var warehouseCode = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == warehouseId)
            .Select(w => w.Code)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The warehouse for this order no longer exists.");
        var codePart = warehouseCode.Length <= MaxCodeWarehouseLength ? warehouseCode : $"W{warehouseId}";

        var rack = await db.Racks.FirstOrDefaultAsync(r => r.WarehouseId == warehouseId && r.IsSystem, cancellationToken);
        if (rack is null)
        {
            rack = new Rack
            {
                WarehouseId = warehouseId,
                Code = $"OV{warehouseId}",
                Name = "Overflow (virtual locations)",
                Columns = 0,
                Levels = 1,
                IsActive = true,
                IsSystem = true
            };
            db.Racks.Add(rack);
        }

        var sequence = await NextSequenceAsync(warehouseId, cancellationToken);
        var position = new PalletPosition
        {
            Rack = rack,
            Column = sequence,
            Level = 1,
            LocationCode = $"VL-{codePart}-{sequence:D6}",
            LocationTypeId = LocationTypeIds.Floor,
            LocationSubtypeId = LocationSubtypeIds.Good,
            ZoneTypeId = ZoneTypeIds.Overflow,
            DistancePriority = 9999,
            MaxPallets = 1,
            IsActive = true,
            IsVirtual = true,
            VirtualStatus = VirtualLocationStatus.Free
        };
        db.PalletPositions.Add(position);
        return position;
    }

    /// <summary>Next number of the warehouse's counter. On MySQL the UPDATE takes the row lock and keeps it until the
    /// transaction ends, so two concurrent creations can never read the same value.</summary>
    private async Task<int> NextSequenceAsync(int warehouseId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            // Take the row lock with the UPDATE itself (an INSERT IGNORE first would take a shared lock that two
            // transactions then both try to upgrade - a deadlock). The row is only created the very first time.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE VirtualLocationSequences SET LastValue = LastValue + 1 WHERE WarehouseId = {warehouseId}", cancellationToken);
                if (updated == 0)
                {
                    var created = await db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT IGNORE INTO VirtualLocationSequences (WarehouseId, LastValue) VALUES ({warehouseId}, 1)", cancellationToken);
                    if (created == 1)
                    {
                        return 1;
                    }

                    continue; // another transaction created it first: increment its row
                }

                var values = await db.Database.SqlQuery<int>(
                    $"SELECT LastValue AS Value FROM VirtualLocationSequences WHERE WarehouseId = {warehouseId}").ToListAsync(cancellationToken);
                return values.Single();
            }

            throw new InvalidOperationException("Could not allocate a virtual location number. Please retry.");
        }

        var sequence = await db.VirtualLocationSequences.FirstOrDefaultAsync(s => s.WarehouseId == warehouseId, cancellationToken);
        if (sequence is null)
        {
            sequence = new VirtualLocationSequence { WarehouseId = warehouseId };
            db.VirtualLocationSequences.Add(sequence);
        }

        sequence.LastValue++;
        return sequence.LastValue;
    }
}
