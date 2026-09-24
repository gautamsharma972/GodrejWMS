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
                MfgMonthParser.Format(l.MfgMonth),
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                l.Status,
                l.Remarks,
                l.Putaways
                    .OrderBy(p => p.PalletPosition.LocationCode)
                    .Select(p => new InwardAllocationLineDto(p.PalletPosition.LocationCode, p.QuantityBoxes, p.AllocationReason, p.IsConfirmed, p.Id))
                    .ToList(),
                IsConfirmed: l.Putaways.All(p => p.IsConfirmed)))
            .ToList();

        return new InwardResultDto(
            transaction.Id,
            transaction.ReferenceNumber,
            transaction.CreatedAt,
            lines,
            IsConfirmed: lines.All(l => l.IsConfirmed),
            IsRejected: transaction.IsRejected);
    }
}
