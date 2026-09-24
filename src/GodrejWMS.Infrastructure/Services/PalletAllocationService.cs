using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>
/// Put-away engine for FMCG pallet storage. Three tiers, in order: (1) top up positions already
/// holding the exact same SKU + PKM + subtype; (2) for a SKU that already has stock/reservations
/// somewhere, consolidate into that rack first (ranked by existing concentration, then by other
/// racks in Code order), across every level, so the same SKU doesn't scatter needlessly; (3) fall
/// back to the original unrestricted best-scoring empty location (subtype, SKU velocity, season,
/// zone) — this also covers a brand-new SKU's very first placement, and anything Tier 2 couldn't
/// place.
///
/// Within a tier, positions are ranked lexicographically, most to least significant: zone/velocity
/// fit (<see cref="ScoreLocation"/>), then Design Code proximity (prefer a rack already holding the
/// same Design Code), then the physical Rack -> Column -> Level fill sequence: a column is filled
/// from Level 1 upward (A-01-01, A-01-02, ...) before moving to the next column. Box weight
/// deliberately does not reorder levels - doing so made fills level-major across the whole rack
/// (A-01-01, A-02-01, A-03-01, ...). This is deliberately independent of <see cref="PalletPosition.DistancePriority"/>, which stays level-major and keeps
/// driving Pullout's separate FIFO picking order - fill order and pick order are different
/// concerns and are allowed to differ.
///
/// Before any of the above: if a material has <see cref="Material.RequirePreferredZone"/> set,
/// candidates are hard-filtered down to <see cref="Material.PreferredZoneTypeId"/> up front (see
/// <c>requiredZoneTypeId</c> in <see cref="AllocateAsync"/>), across every tier including topping
/// up the material's own existing stock. This defaults to off, in which case
/// <see cref="Material.PreferredZoneTypeId"/> stays purely a soft <see cref="ScoreLocation"/> nudge.
/// </summary>
public class PalletAllocationService(IApplicationDbContext db, IDateTimeProvider clock) : IPalletAllocationService
{
    public async Task<InwardAllocationResult> AllocateAsync(
        int materialId,
        decimal quantityBoxes,
        int mfgMonth,
        int stockSubtypeId = LocationSubtypeIds.Good,
        CancellationToken cancellationToken = default)
    {
        var material = await db.Materials
            .AsNoTracking()
            .FirstAsync(m => m.Id == materialId, cancellationToken);
        var activeSeasonId = await GetActiveSeasonAsync(cancellationToken);
        var stockSubtypeCode = await db.LocationSubtypes
            .AsNoTracking()
            .Where(s => s.Id == stockSubtypeId)
            .Select(s => s.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown";
        var remaining = quantityBoxes;
        var lines = new List<InwardAllocationLine>();
        var reservedByPosition = new Dictionary<int, decimal>();

        var pendingReservations = await db.InwardPutaways
            .AsNoTracking()
            .Where(p => !p.IsConfirmed)
            .Select(p => new PendingReservation(
                p.PalletPositionId,
                p.PalletPosition.RackId,
                p.InwardTransactionLine.MaterialId,
                p.InwardTransactionLine.MfgMonth,
                p.PalletPosition.LocationSubtypeId,
                p.QuantityBoxes))
            .ToListAsync(cancellationToken);

        // Hard constraint: a material that requires its preferred zone can never be placed
        // outside it - not even to top up its own existing stock or consolidate within a rack.
        // (When RequirePreferredZone is false, PreferredZoneTypeId stays a soft ScoreLocation
        // nudge and every position here remains eligible.)
        var requiredZoneTypeId = material.RequirePreferredZone ? material.PreferredZoneTypeId : null;

        var candidatePositions = await db.PalletPositions
            .Where(p => p.IsActive && p.LocationSubtypeId == stockSubtypeId
                && (requiredZoneTypeId == null || p.ZoneTypeId == requiredZoneTypeId))
            .Include(p => p.Rack)
            .Include(p => p.StockBatches)
            .ToListAsync(cancellationToken);

        var topUpPositions = candidatePositions
            .Where(p => HasSameSkuPkmStock(p, materialId, mfgMonth, stockSubtypeId) || HasSameSkuPkmReservation(p.Id, pendingReservations, materialId, mfgMonth, stockSubtypeId))
            .OrderBy(p => ScoreLocation(p, material, activeSeasonId, mfgMonth, oldestMfgMonth: null));

        foreach (var position in topUpPositions)
        {
            if (remaining <= 0)
            {
                break;
            }

            var spare = SpareCapacity(position, material, pendingReservations, reservedByPosition);
            if (spare <= 0)
            {
                continue;
            }

            var take = Math.Min(remaining, spare);
            AddReservation(reservedByPosition, position.Id, take);
            remaining -= take;
            lines.Add(new InwardAllocationLine(position.LocationCode, take, "Same SKU + PKM top-up"));
        }

        if (remaining > 0)
        {
            var oldestMfgMonth = await db.StockBatches
                .Where(b => b.MaterialId == materialId && b.StockSubtypeId == stockSubtypeId && b.QuantityBoxes > 0)
                .MinAsync(b => (int?)b.MfgMonth, cancellationToken);

            var racksWithSameDesignType = await GetRacksWithSameDesignTypeAsync(candidatePositions, pendingReservations, material, cancellationToken);

            var hasAnchor = candidatePositions.Any(p => p.StockBatches.Any(b => b.MaterialId == materialId && b.QuantityBoxes > 0))
                || pendingReservations.Any(r => r.MaterialId == materialId && r.StockSubtypeId == stockSubtypeId && r.QuantityBoxes > 0);

            if (hasAnchor)
            {
                remaining = FillSameRackConsolidation(
                    candidatePositions, pendingReservations, reservedByPosition, lines,
                    materialId, stockSubtypeId, material, activeSeasonId, mfgMonth, oldestMfgMonth,
                    remaining);
            }

            foreach (var position in candidatePositions
                .Where(p => !p.StockBatches.Any() && !HasAnyReservation(p.Id, pendingReservations) && !reservedByPosition.ContainsKey(p.Id))
                .OrderBy(p => ScoreLocation(p, material, activeSeasonId, mfgMonth, oldestMfgMonth))
                .ThenByDescending(p => racksWithSameDesignType.Contains(p.RackId))
                .ThenBy(p => p.Rack.Code)
                .ThenBy(p => p.Column)
                .ThenBy(p => p.Level))
            {
                if (remaining <= 0)
                {
                    break;
                }

                var spare = SpareCapacity(position, material, pendingReservations, reservedByPosition);
                if (spare <= 0)
                {
                    continue;
                }

                var take = Math.Min(remaining, spare);
                AddReservation(reservedByPosition, position.Id, take);
                remaining -= take;
                lines.Add(new InwardAllocationLine(position.LocationCode, take,
                    AllocationReason(position, material, activeSeasonId, mfgMonth, oldestMfgMonth, racksWithSameDesignType.Contains(position.RackId))));
            }
        }

        var allocated = quantityBoxes - remaining;
        var status = remaining <= 0
            ? AllocationStatus.Fulfilled
            : allocated > 0
                ? AllocationStatus.Partial
                : AllocationStatus.Failed;

        var remarks = status == AllocationStatus.Fulfilled
            ? null
            : requiredZoneTypeId.HasValue
                ? $"No eligible capacity in this SKU's required zone - {remaining:0.###} box(es) could not be reserved in a matching {stockSubtypeCode} location."
                : $"Warehouse at capacity - {remaining:0.###} box(es) could not be reserved in a matching {stockSubtypeCode} location.";

        return new InwardAllocationResult(status, allocated, lines, remarks);
    }

    /// <summary>
    /// Tier 2: for a SKU that already has stock/reservations somewhere, prefer consolidating new
    /// quantity into the rack(s) it's already concentrated in — ranked by existing quantity of
    /// this material (descending), then every other rack by Code — across every level. Candidates
    /// must be empty or already hold this same material (never a different one). Returns the
    /// quantity still remaining.
    /// </summary>
    private static decimal FillSameRackConsolidation(
        List<PalletPosition> candidatePositions,
        IReadOnlyList<PendingReservation> pendingReservations,
        Dictionary<int, decimal> reservedByPosition,
        List<InwardAllocationLine> lines,
        int materialId,
        int stockSubtypeId,
        Material material,
        int activeSeasonId,
        int incomingMfgMonth,
        int? oldestMfgMonth,
        decimal remaining)
    {
        var rackQuantities = new Dictionary<int, decimal>();
        foreach (var position in candidatePositions)
        {
            var qty = position.StockBatches.Where(b => b.MaterialId == materialId && b.QuantityBoxes > 0).Sum(b => b.QuantityBoxes);
            if (qty > 0)
            {
                rackQuantities[position.RackId] = rackQuantities.GetValueOrDefault(position.RackId) + qty;
            }
        }

        foreach (var reservation in pendingReservations.Where(r => r.MaterialId == materialId && r.StockSubtypeId == stockSubtypeId && r.QuantityBoxes > 0))
        {
            rackQuantities[reservation.RackId] = rackQuantities.GetValueOrDefault(reservation.RackId) + reservation.QuantityBoxes;
        }

        var rackCodeById = candidatePositions
            .GroupBy(p => p.RackId)
            .ToDictionary(g => g.Key, g => g.First().Rack.Code);

        var rankedRackIds = rackCodeById.Keys
            .Where(id => rackQuantities.GetValueOrDefault(id) > 0)
            .OrderByDescending(id => rackQuantities[id])
            .ThenBy(id => rackCodeById[id], StringComparer.Ordinal)
            .Concat(rackCodeById.Keys
                .Where(id => rackQuantities.GetValueOrDefault(id) <= 0)
                .OrderBy(id => rackCodeById[id], StringComparer.Ordinal))
            .ToList();

        foreach (var rackId in rankedRackIds)
        {
            if (remaining <= 0)
            {
                break;
            }

            var rackCandidates = candidatePositions
                .Where(p => p.RackId == rackId && IsConsolidationEligible(p, materialId, pendingReservations))
                .OrderBy(p => ScoreLocation(p, material, activeSeasonId, incomingMfgMonth, oldestMfgMonth))
                .ThenBy(p => p.Column)
                .ThenBy(p => p.Level);

            foreach (var position in rackCandidates)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var spare = SpareCapacity(position, material, pendingReservations, reservedByPosition);
                if (spare <= 0)
                {
                    continue;
                }

                var take = Math.Min(remaining, spare);
                AddReservation(reservedByPosition, position.Id, take);
                remaining -= take;
                lines.Add(new InwardAllocationLine(position.LocationCode, take, "Same-rack consolidation"));
            }
        }

        return remaining;
    }

    private static bool IsConsolidationEligible(PalletPosition position, int materialId, IReadOnlyList<PendingReservation> pendingReservations)
    {
        var hasOtherMaterialStock = position.StockBatches.Any(b => b.QuantityBoxes > 0 && b.MaterialId != materialId);
        var hasOtherMaterialReservation = pendingReservations.Any(r => r.PalletPositionId == position.Id && r.QuantityBoxes > 0 && r.MaterialId != materialId);
        return !hasOtherMaterialStock && !hasOtherMaterialReservation;
    }

    private static bool HasSameSkuPkmStock(PalletPosition position, int materialId, int mfgMonth, int stockSubtypeId) =>
        position.StockBatches.Any(b => b.MaterialId == materialId && b.MfgMonth == mfgMonth && b.StockSubtypeId == stockSubtypeId && b.QuantityBoxes > 0);

    private static bool HasSameSkuPkmReservation(int palletPositionId, IReadOnlyList<PendingReservation> reservations, int materialId, int mfgMonth, int stockSubtypeId) =>
        reservations.Any(r => r.PalletPositionId == palletPositionId && r.MaterialId == materialId && r.MfgMonth == mfgMonth && r.StockSubtypeId == stockSubtypeId && r.QuantityBoxes > 0);

    private static bool HasAnyReservation(int palletPositionId, IReadOnlyList<PendingReservation> reservations) =>
        reservations.Any(r => r.PalletPositionId == palletPositionId && r.QuantityBoxes > 0);

    private static void AddReservation(IDictionary<int, decimal> reservations, int palletPositionId, decimal quantityBoxes)
    {
        reservations[palletPositionId] = reservations.TryGetValue(palletPositionId, out var existing)
            ? existing + quantityBoxes
            : quantityBoxes;
    }

    private static decimal SpareCapacity(
        PalletPosition position,
        Material material,
        IReadOnlyList<PendingReservation> pendingReservations,
        IReadOnlyDictionary<int, decimal> reservedByPosition)
    {
        var occupied = position.StockBatches.Sum(b => b.QuantityBoxes)
            + pendingReservations.Where(r => r.PalletPositionId == position.Id).Sum(r => r.QuantityBoxes)
            + (reservedByPosition.TryGetValue(position.Id, out var reserved) ? reserved : 0m);
        var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position, material);
        return effectiveCapacity - occupied;
    }

    private async Task<int> GetActiveSeasonAsync(CancellationToken cancellationToken)
    {
        var month = clock.UtcNow.Month;
        return await db.SeasonMonthMaps
            .AsNoTracking()
            .Where(m => m.Month == month)
            .Select(m => (int?)m.SeasonId)
            .FirstOrDefaultAsync(cancellationToken) ?? SeasonIds.Rainy;
    }

    private static string AllocationReason(
        PalletPosition position,
        Material material,
        int activeSeasonId,
        int incomingMfgMonth,
        int? oldestMfgMonth,
        bool sameDesignCodeInRack)
    {
        var isCurrentSeason = material.SeasonId == activeSeasonId;
        var isFast = material.MovementTypeId == SkuMovementTypeIds.FastMoving;

        if (position.ZoneTypeId == ZoneTypeIds.DispatchNear && isFast && isCurrentSeason)
        {
            return "Fast current-season SKU in dispatch-near zone";
        }

        if (position.ZoneTypeId == ZoneTypeIds.Fast && isFast)
        {
            return "Fast-moving SKU in fast zone";
        }

        if (position.ZoneTypeId == ZoneTypeIds.Seasonal && isCurrentSeason)
        {
            return "Current-season SKU in seasonal zone";
        }

        if (material.PreferredZoneTypeId.HasValue && position.ZoneTypeId == material.PreferredZoneTypeId.Value)
        {
            return material.RequirePreferredZone
                ? "This SKU's required zone"
                : "Preferred zone for this SKU";
        }

        if (sameDesignCodeInRack)
        {
            return "Near existing stock of the same Design Code";
        }

        if (oldestMfgMonth.HasValue && incomingMfgMonth > oldestMfgMonth.Value)
        {
            return "Empty location selected after preserving older stock priority";
        }

        return "Next in fill sequence (column by column, bottom to top)";
    }

    /// <summary>
    /// Zone/velocity/season fit only - the dominant ranking tier. Design Code proximity, weight,
    /// and the Rack -&gt; Column -&gt; Level fill sequence are applied as separate <c>ThenBy</c>
    /// tie-break tiers by the caller (see the class-level remarks), not folded in here, so each
    /// stays a clean, independently reasoned-about priority rather than a magic-number blend.
    /// </summary>
    private static int ScoreLocation(
        PalletPosition position,
        Material material,
        int activeSeasonId,
        int incomingMfgMonth,
        int? oldestMfgMonth)
    {
        var isCurrentSeason = material.SeasonId == activeSeasonId;
        var isFast = material.MovementTypeId == SkuMovementTypeIds.FastMoving;

        var score = position.ZoneTypeId switch
        {
            ZoneTypeIds.DispatchNear when isFast && isCurrentSeason => 0,
            ZoneTypeIds.Fast when isFast => 20,
            ZoneTypeIds.Seasonal when isCurrentSeason => 30,
            ZoneTypeIds.Reserve when !isFast => 45,
            ZoneTypeIds.DispatchNear => 80,
            ZoneTypeIds.Fast => 90,
            ZoneTypeIds.Seasonal => 95,
            _ => 110
        };

        // Soft preference only: nudges the score, never gates placement the way the zone
        // switch above does, so an unmatched/absent preference can never block a valid put-away.
        if (material.PreferredZoneTypeId.HasValue && position.ZoneTypeId == material.PreferredZoneTypeId.Value)
        {
            score -= 12;
        }

        if (oldestMfgMonth.HasValue && incomingMfgMonth > oldestMfgMonth.Value)
        {
            score += 50;
        }

        if (position.LocationTypeId != LocationTypeIds.Rack)
        {
            score += 25;
        }

        return score;
    }

    /// <summary>
    /// Racks that already hold stock or a pending reservation of a material sharing this
    /// material's Design Code (soft priority: prefer nearby placement, but never the same
    /// location - that would violate the no-mixing rule, already enforced by the candidate
    /// filters upstream).
    /// </summary>
    private async Task<HashSet<int>> GetRacksWithSameDesignTypeAsync(
        List<PalletPosition> candidatePositions,
        IReadOnlyList<PendingReservation> pendingReservations,
        Material material,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(material.DesignType))
        {
            return [];
        }

        var involvedMaterialIds = candidatePositions
            .SelectMany(p => p.StockBatches.Where(b => b.QuantityBoxes > 0).Select(b => b.MaterialId))
            .Concat(pendingReservations.Where(r => r.QuantityBoxes > 0).Select(r => r.MaterialId))
            .Distinct()
            .ToList();

        if (involvedMaterialIds.Count == 0)
        {
            return [];
        }

        var designTypeByMaterialId = await db.Materials
            .AsNoTracking()
            .Where(m => involvedMaterialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.DesignType })
            .ToDictionaryAsync(m => m.Id, m => m.DesignType, cancellationToken);

        return candidatePositions
            .Where(p => p.StockBatches.Any(b => b.QuantityBoxes > 0 && designTypeByMaterialId.GetValueOrDefault(b.MaterialId) == material.DesignType))
            .Select(p => p.RackId)
            .Concat(pendingReservations
                .Where(r => r.QuantityBoxes > 0 && designTypeByMaterialId.GetValueOrDefault(r.MaterialId) == material.DesignType)
                .Select(r => r.RackId))
            .ToHashSet();
    }

    private sealed record PendingReservation(int PalletPositionId, int RackId, int MaterialId, int MfgMonth, int StockSubtypeId, decimal QuantityBoxes);
}
