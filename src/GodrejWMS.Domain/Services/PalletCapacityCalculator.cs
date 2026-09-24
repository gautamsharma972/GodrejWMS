using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Domain.Services;

/// <summary>
/// Single source of truth for "how many boxes of a given Material can a PalletPosition
/// physically hold" — shared by the put-away engine and the manual re-route features so the
/// formula never drifts between call sites.
/// </summary>
public static class PalletCapacityCalculator
{
    /// <summary>
    /// Effective capacity in boxes = the lower of the position's physical capacity and the
    /// SKU-specific capacity (MaxPallets * material's PalletCapacityBoxes). Falls back to the
    /// physical capacity alone when the material has no PalletCapacityBoxes configured.
    /// </summary>
    public static decimal EffectiveCapacityBoxes(int capacityBoxes, int maxPallets, int palletCapacityBoxes)
    {
        var skuCapacity = palletCapacityBoxes > 0 ? maxPallets * palletCapacityBoxes : capacityBoxes;
        return Math.Min(capacityBoxes, skuCapacity);
    }

    public static decimal EffectiveCapacityBoxes(PalletPosition position, Material material) =>
        EffectiveCapacityBoxes(position.CapacityBoxes, position.MaxPallets, material.PalletCapacityBoxes);
}
