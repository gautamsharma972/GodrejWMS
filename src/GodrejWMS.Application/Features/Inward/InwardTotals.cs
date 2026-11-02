using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>The GRN's inbound volume and allocation percentage. "Uploaded" is every box the user submitted -
/// the saved lines plus any line that could not be saved (unknown material, unreadable row) - so the volume
/// and the percentage reflect the whole upload, not just the part that found locations.</summary>
public static class InwardTotals
{
    public static decimal Uploaded(InwardDetailDto detail) =>
        detail.Lines.Sum(l => l.RequestedQuantityBoxes) + (detail.UnsavedLines?.Sum(u => u.QuantityBoxes) ?? 0m);

    public static decimal Allocated(InwardDetailDto detail) => detail.Lines.Sum(l => l.AllocatedQuantityBoxes);

    public static decimal UnsavedBoxes(InwardDetailDto detail) => detail.UnsavedLines?.Sum(u => u.QuantityBoxes) ?? 0m;

    /// <summary>For a just-submitted result, unknown-material lines are already among <see cref="InwardResultDto.Lines"/>
    /// (shown as failed), so only the unreadable rows are added on top.</summary>
    public static decimal Uploaded(InwardResultDto result) =>
        result.Lines.Sum(l => l.RequestedQuantityBoxes)
        + (result.UnsavedLines?.Where(u => u.Kind == InwardUnsavedLineKind.UnreadableRow).Sum(u => u.QuantityBoxes) ?? 0m);

    public static decimal Percent(decimal allocated, decimal uploaded) =>
        uploaded > 0 ? Math.Min(100m, allocated / uploaded * 100m) : 0m;
}
