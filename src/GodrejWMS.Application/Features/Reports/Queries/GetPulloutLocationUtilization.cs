using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Pullout Reports - "Pick Location Utilization" tab: which zones/location types stock is
/// actually picked from most, within an optional date range - validates whether FEFO plus
/// distance-priority tuning is drawing from the intended locations.</summary>
public sealed record GetPulloutLocationUtilizationQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<PulloutLocationUtilizationRowDto>>;

public sealed class GetPulloutLocationUtilizationHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutLocationUtilizationQuery, IReadOnlyList<PulloutLocationUtilizationRowDto>>
{
    public async Task<IReadOnlyList<PulloutLocationUtilizationRowDto>> Handle(
        GetPulloutLocationUtilizationQuery request, CancellationToken cancellationToken)
    {
        var query = db.PulloutPicks.AsNoTracking().Where(p => p.PulloutTransactionLine.PulloutTransaction.IsConfirmed);

        if (request.FromDate.HasValue)
        {
            query = query.Where(p => p.PulloutTransactionLine.PulloutTransaction.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(p => p.PulloutTransactionLine.PulloutTransaction.CreatedAt <= request.ToDate.Value);
        }

        var picks = await query
            .Select(p => new
            {
                p.PalletPosition.ZoneType.Code,
                ZoneName = p.PalletPosition.ZoneType.DisplayName,
                LocationTypeCode = p.PalletPosition.LocationType.Code,
                LocationTypeName = p.PalletPosition.LocationType.DisplayName,
                p.QuantityBoxes
            })
            .ToListAsync(cancellationToken);

        return picks
            .GroupBy(p => new { p.Code, p.ZoneName, p.LocationTypeCode, p.LocationTypeName })
            .Select(g => new PulloutLocationUtilizationRowDto(
                g.Key.Code, g.Key.ZoneName, g.Key.LocationTypeCode, g.Key.LocationTypeName,
                g.Count(), g.Sum(p => p.QuantityBoxes)))
            .OrderByDescending(r => r.PickCount)
            .ToList();
    }
}
