using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>
/// Turns a GRN (from the allocation-result panel or the detail page) into the rows of the inward
/// Excel download: one row per put-away location, or one location-less row for a line that got none.
/// </summary>
public static class InwardDownloadMapper
{
    public static IReadOnlyList<InwardDownloadRow> FromDetail(InwardDetailDto detail) =>
        detail.Lines
            .SelectMany(l => Rows(
                detail.ReferenceNumber, l.MaterialNumber, l.MaterialDescription, l.MfgMonthLabel,
                l.RequestedQuantityBoxes, l.AllocatedQuantityBoxes, l.Status,
                l.Putaways.Select(p => (p.LocationCode, p.QuantityBoxes, p.IsConfirmed)), detail.IsRejected))
            .ToList();

    public static IReadOnlyList<InwardDownloadRow> FromResult(InwardResultDto result) =>
        result.Lines
            .SelectMany(l => Rows(
                result.ReferenceNumber, l.MaterialNumber, l.MaterialDescription, l.MfgMonthLabel,
                l.RequestedQuantityBoxes, l.AllocatedQuantityBoxes, l.Status,
                l.AllocatedTo.Select(a => (a.LocationCode, a.QuantityBoxes, a.IsConfirmed)), result.IsRejected))
            .ToList();

    private static IEnumerable<InwardDownloadRow> Rows(
        string reference, long materialNumber, string description, string mfgMonthLabel,
        decimal requested, decimal allocated, AllocationStatus status,
        IEnumerable<(string LocationCode, decimal QuantityBoxes, bool IsConfirmed)> locations, bool isRejected)
    {
        var placed = locations.ToList();
        if (placed.Count == 0)
        {
            yield return new InwardDownloadRow(reference, materialNumber, description, mfgMonthLabel,
                requested, allocated, status.ToString(), string.Empty, 0m, isRejected ? "Rejected" : "No location");
            yield break;
        }

        foreach (var (locationCode, quantity, isConfirmed) in placed)
        {
            yield return new InwardDownloadRow(reference, materialNumber, description, mfgMonthLabel,
                requested, allocated, status.ToString(), locationCode, quantity,
                isRejected ? "Rejected" : isConfirmed ? "Confirmed" : "Reserved");
        }
    }
}
