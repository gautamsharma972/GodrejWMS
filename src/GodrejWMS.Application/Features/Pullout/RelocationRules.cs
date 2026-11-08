using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout;

/// <summary>Rules for putting boxes into a location the Picker chose (the remainder after a partial pick, a corrected pick source).</summary>
internal static class RelocationRules
{
    /// <summary>Null when <paramref name="quantityBoxes"/> of the material may go to <paramref name="destination"/>; otherwise a
    /// business-friendly reason. The destination must exist (loaded), be active, be an ordinary good-stock location of the same
    /// warehouse and not the source, and have room under the pallet-size rules: Max Pallets x the smallest pallet size of the
    /// materials kept there (virtual locations included).</summary>
    public static async Task<string?> ValidateDestinationAsync(
        IApplicationDbContext db, PalletPosition? destination, int materialId, int sourcePalletPositionId, int warehouseId,
        decimal quantityBoxes, CancellationToken cancellationToken)
    {
        if (destination is null)
        {
            return "The destination location was not found.";
        }

        if (destination.Id == sourcePalletPositionId)
        {
            return "The destination must be a different location from the one the stock is in.";
        }

        if (!destination.IsActive)
        {
            return $"Location {destination.LocationCode} is not active.";
        }

        var destinationWarehouseId = destination.Rack?.WarehouseId
            ?? await db.Racks.AsNoTracking().Where(r => r.Id == destination.RackId).Select(r => r.WarehouseId).FirstAsync(cancellationToken);
        if (destinationWarehouseId != warehouseId)
        {
            return $"Location {destination.LocationCode} belongs to a different warehouse.";
        }

        if (destination.LocationSubtypeId != LocationSubtypeIds.Good)
        {
            return $"Location {destination.LocationCode} is not a good-stock location.";
        }

        var held = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == destination.Id && b.QuantityBoxes > 0 && b.Material.IsActive)
            .Select(b => new { b.QuantityBoxes, b.Material.PalletCapacityBoxes })
            .ToListAsync(cancellationToken);
        var reserved = await db.InwardPutaways.AsNoTracking()
            .Where(p => p.PalletPositionId == destination.Id && !p.IsConfirmed && p.ExcludedLocation == null
                && !p.InwardTransactionLine.InwardTransaction.IsRejected)
            .SumAsync(p => (decimal?)p.QuantityBoxes, cancellationToken) ?? 0m;
        var incomingPalletSize = await db.Materials.AsNoTracking()
            .Where(m => m.Id == materialId).Select(m => m.PalletCapacityBoxes).FirstAsync(cancellationToken);

        var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(
            destination.MaxPallets, held.Select(h => h.PalletCapacityBoxes).Append(incomingPalletSize));
        var occupied = held.Sum(h => h.QuantityBoxes) + reserved;
        if (occupied + quantityBoxes > capacity)
        {
            return $"Location {destination.LocationCode} cannot take {quantityBoxes:0.###} box(es): it already holds {occupied:0.###} " +
                   $"of its {capacity:0.###}-box capacity (pallet size rules).";
        }

        return null;
    }
}
