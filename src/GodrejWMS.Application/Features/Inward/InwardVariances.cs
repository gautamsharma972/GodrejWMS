using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>Shared rules for the damage / shortage / mismatch / excess records a supervisor keeps against a GRN.</summary>
public static class InwardVariances
{
    public const string LedgerReferenceType = "GRN_VARIANCE";

    /// <summary>The location a supervisor chose for damaged / mismatched boxes: active, in the GRN's warehouse, not GIT; a mismatch is Good stock so it needs a Good location,
    /// damaged boxes may also go to a Damage location.</summary>
    public static async Task<PalletPosition> LoadDestinationAsync(
        IApplicationDbContext db, int positionId, int warehouseId, InwardVarianceType type, CancellationToken cancellationToken)
    {
        var position = await db.PalletPositions.Include(p => p.Rack).FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken)
            ?? throw new InvalidOperationException("The selected location was not found.");
        if (!position.IsActive || !position.Rack.IsActive)
        {
            throw new InvalidOperationException($"Location {position.LocationCode} is not active.");
        }

        if (position.Rack.WarehouseId != warehouseId)
        {
            throw new InvalidOperationException($"Location {position.LocationCode} belongs to a different warehouse.");
        }

        if (position.ZoneTypeId == ZoneTypeIds.Git
            || (position.LocationSubtypeId != LocationSubtypeIds.Good && !(type == InwardVarianceType.Damage && position.LocationSubtypeId == LocationSubtypeIds.Damage)))
        {
            throw new InvalidOperationException($"Location {position.LocationCode} cannot hold {(type == InwardVarianceType.Damage ? "damaged" : "good")} stock.");
        }

        return position;
    }

    /// <summary>Saves; a lost race against another user (concurrency token, unique key) becomes a clear message instead of a database error.</summary>
    public static async Task SaveAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("This GRN was just changed by another user. Reload it and try again.");
        }
    }

    /// <summary>Damage, Shortage and Mismatch account for part of the inbound quantity; Excess is extra material outside it.</summary>
    public static bool CountsAgainstInbound(InwardVarianceType type) => type != InwardVarianceType.Excess;

    /// <summary>Boxes of each material + PKM that records account for (Damage / Shortage / Mismatch only): they are neither put away nor "missing".</summary>
    public static async Task<Dictionary<(int MaterialId, int MfgMonth), decimal>> AccountedAsync(
        IApplicationDbContext db, int transactionId, CancellationToken cancellationToken)
    {
        var rows = await db.InwardLineVariances.AsNoTracking()
            .Where(v => v.InwardTransactionId == transactionId && v.Type != InwardVarianceType.Excess)
            .GroupBy(v => new { v.MaterialId, v.MfgMonth })
            .Select(g => new { g.Key.MaterialId, g.Key.MfgMonth, Boxes = g.Sum(v => v.QuantityBoxes) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => (r.MaterialId, r.MfgMonth), r => r.Boxes);
    }

    /// <summary>Re-derives a line's status: its put-away plus the boxes recorded as damage / shortage / mismatch against the inbound quantity.</summary>
    public static void RefreshLineStatus(InwardTransactionLine line, decimal accountedByRecords)
    {
        var covered = line.AllocatedQuantityBoxes + accountedByRecords;
        line.Status = covered >= line.RequestedQuantityBoxes
            ? Domain.Enums.AllocationStatus.Fulfilled
            : line.AllocatedQuantityBoxes > 0 ? Domain.Enums.AllocationStatus.Partial : Domain.Enums.AllocationStatus.Failed;
        line.Remarks = line.Status == Domain.Enums.AllocationStatus.Fulfilled
            ? (accountedByRecords > 0 ? $"{line.AllocatedQuantityBoxes:0.###} put away, {accountedByRecords:0.###} box(es) recorded as damage / shortage / mismatch." : null)
            : $"Put away {line.AllocatedQuantityBoxes:0.###} of {line.RequestedQuantityBoxes:0.###} box(es)"
              + (accountedByRecords > 0 ? $" ({accountedByRecords:0.###} recorded as damage / shortage / mismatch)." : ".");
    }
}
