using GodrejWMS.Application.Common;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>Builds the API-facing result shape from a loaded <see cref="InwardTransaction"/>.</summary>
public static class InwardResultMapper
{
    public static InwardResultDto ToResult(InwardTransaction transaction)
    {
        var lines = transaction.Lines
            .OrderBy(l => l.Id)
            .Select(l => new InwardLineResultDto(
                l.Material.MaterialNumber,
                l.Material.Description,
                MfgMonthParser.FormatOrNoPkm(l.MfgMonth),
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                l.Status,
                l.Remarks,
                l.Putaways
                    .OrderBy(p => p.PalletPosition.LocationCode)
                    .Select(p => new InwardAllocationLineDto(p.PalletPosition.LocationCode, p.QuantityBoxes, p.AllocationReason, p.IsConfirmed, p.Id))
                    .ToList(),
                // Putaways.All(...) is vacuously true for a line with zero putaways (a totally
                // failed allocation, or one cleared out by rejection) - Any() guards that so an
                // unconfirmed/rejected GRN never reports as confirmed.
                IsConfirmed: l.Putaways.Count > 0 && l.Putaways.All(p => p.IsConfirmed)))
            .ToList();

        return new InwardResultDto(
            transaction.Id,
            transaction.ReferenceNumber,
            transaction.CreatedAt,
            lines,
            IsConfirmed: lines.Count > 0 && lines.All(l => l.IsConfirmed),
            IsRejected: transaction.IsRejected);
    }
}
