using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Domain.Services;

/// <summary>
/// Single source of truth for "how many boxes of a given Material can a PalletPosition
/// physically hold" — shared by the put-away engine and the manual re-route features so the
/// formula never drifts between call sites. A location's capacity is driven entirely by which
/// material occupies it (a position only ever holds one material at a time): MaxPallets x that
/// material's own PalletCapacityBoxes ("Pallet Size" in Material Master) — there is no
/// location-level box-capacity override.
/// </summary>
public static class PalletCapacityCalculator
{
    public static decimal EffectiveCapacityBoxes(int maxPallets, int palletCapacityBoxes) =>
        maxPallets * palletCapacityBoxes;

    public static decimal EffectiveCapacityBoxes(PalletPosition position, Material material) =>
        EffectiveCapacityBoxes(position.MaxPallets, material.PalletCapacityBoxes);
}
