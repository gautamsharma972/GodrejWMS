using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.MaterialConversions.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.MaterialConversions;

/// <summary>Shared by the proposal query and the submit command: which stock of the old code can be converted, and what the user should know first.</summary>
internal static class MaterialConversionRules
{
    /// <summary>Stock that can be converted: Good subtype, active Good location in the warehouse, not the GIT staging location.</summary>
    public static IQueryable<StockBatch> ConvertibleBatches(IApplicationDbContext db, int materialId, int warehouseId) =>
        db.StockBatches
            .Where(b => b.MaterialId == materialId
                && b.StockSubtypeId == LocationSubtypeIds.Good
                && b.QuantityBoxes > 0
                && b.PalletPosition.IsActive
                && b.PalletPosition.LocationSubtypeId == LocationSubtypeIds.Good
                && b.PalletPosition.Rack.WarehouseId == warehouseId
                && b.PalletPosition.ZoneTypeId != ZoneTypeIds.Git);

    public static async Task<(Material From, Material To)> LoadMaterialsAsync(IApplicationDbContext db, int fromId, int toId, CancellationToken ct)
    {
        if (fromId == toId)
        {
            throw new InvalidOperationException("The old and the new material code must be different.");
        }

        var from = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == fromId, ct)
            ?? throw new InvalidOperationException("The old material code was not found.");
        var to = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == toId, ct)
            ?? throw new InvalidOperationException("The new material code was not found.");
        if (!to.IsActive)
        {
            throw new InvalidOperationException($"The new material code {to.MaterialNumber} is inactive in Material Master. Activate it first.");
        }

        return (from, to);
    }

    /// <summary>Non-blocking things to check before converting: attribute differences, mixed / over-full locations afterwards, open documents that still hold the old code.</summary>
    public static async Task<List<string>> BuildWarningsAsync(
        IApplicationDbContext db, Material from, Material to, int warehouseId,
        IReadOnlyList<(int PositionId, string Location, decimal Quantity, decimal Available)> cells, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (from.DesignType != to.DesignType) warnings.Add($"Design type differs ({from.DesignType} → {to.DesignType}).");
        if (from.AopDescriptionId != to.AopDescriptionId) warnings.Add("AOP description differs: future put-away of the new code follows its own AOP location preference.");
        if (from.PreferredZoneTypeId != to.PreferredZoneTypeId || from.RequirePreferredZone != to.RequirePreferredZone) warnings.Add("Preferred zone settings differ between the two codes.");
        if (from.PalletCapacityBoxes != to.PalletCapacityBoxes) warnings.Add($"Pallet size differs ({from.PalletCapacityBoxes} → {to.PalletCapacityBoxes}).");
        if (from.PackSize != to.PackSize) warnings.Add($"Pack size differs ({from.PackSize} → {to.PackSize}).");

        var openPullouts = await db.PulloutTransactionLines.AsNoTracking()
            .Where(l => l.MaterialId == from.Id && l.PulloutTransaction.WarehouseId == warehouseId
                && !l.PulloutTransaction.IsRejected && l.PulloutTransaction.ConfirmedAt == null && l.Picks.Any())
            .Select(l => l.PulloutTransaction.ReferenceNumber).Distinct().ToListAsync(ct);
        if (openPullouts.Count > 0)
        {
            warnings.Add($"Open pull-out(s) still hold picks of {from.MaterialNumber}: {string.Join(", ", openPullouts.Take(5))}. Their picks stay under the old code; boxes handed back by a reject / refresh return under the old code.");
        }

        var positionIds = cells.Select(c => c.PositionId).ToList();
        if (positionIds.Count == 0)
        {
            return warnings;
        }

        var grns = await db.InwardPutaways.AsNoTracking()
            .Where(p => positionIds.Contains(p.PalletPositionId) && p.InwardTransactionLine.MaterialId == from.Id
                && p.IsConfirmed && !p.IsExcludedLocked && p.ExcludedLocation == null
                && p.InwardTransactionLine.InwardTransaction.PostedAt != null && !p.InwardTransactionLine.InwardTransaction.IsRejected)
            .Select(p => p.InwardTransactionLine.InwardTransaction.ReferenceNumber).Distinct().ToListAsync(ct);
        if (grns.Count > 0)
        {
            warnings.Add($"GRN(s) not confirmed yet still expect {from.MaterialNumber} in these locations: {string.Join(", ", grns.Take(5))}. Converting that stock will show in Stock Reconciliation as 'Put-away without stock' until they are confirmed.");
        }

        var held = await db.StockBatches.AsNoTracking()
            .Where(b => positionIds.Contains(b.PalletPositionId) && b.QuantityBoxes > 0)
            .Select(b => new { b.PalletPositionId, b.MaterialId, b.QuantityBoxes })
            .ToListAsync(ct);
        var maxPallets = await db.PalletPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.MaxPallets, ct);
        foreach (var cell in cells)
        {
            var inCell = held.Where(h => h.PalletPositionId == cell.PositionId).ToList();
            var others = inCell.Where(h => h.MaterialId != from.Id && h.MaterialId != to.Id).Select(h => h.MaterialId).Distinct().Count();
            var partial = cell.Quantity < cell.Available;
            if (others > 0 || partial)
            {
                warnings.Add(others > 0
                    ? $"{cell.Location} also holds another material."
                    : $"{cell.Location} will hold both {from.MaterialNumber} and {to.MaterialNumber} (Stock Reconciliation lists a location with several materials).");
            }

            var total = inCell.Sum(h => h.QuantityBoxes);
            var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(maxPallets.GetValueOrDefault(cell.PositionId, 1), to.PalletCapacityBoxes);
            if (total > capacity)
            {
                warnings.Add($"{cell.Location} holds {total:0.###} box(es), more than {capacity:0.###} for a pallet of {to.MaterialNumber}.");
            }
        }

        return warnings.Distinct().ToList();
    }
}
