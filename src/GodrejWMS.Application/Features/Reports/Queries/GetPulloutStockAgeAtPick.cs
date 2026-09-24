using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Pullout Reports - "Stock Age at Pick" tab: how old the picked manufacturing-month
/// batch was (in days) at the moment of pick, within an optional date range on the parent
/// transaction's <c>CreatedAt</c>. The FEFO engine always picks oldest-first by construction, so
/// this reads as a staleness trend rather than a "violations" report - see
/// PalletAllocationService's pick-ordering doc comment. Note there's no per-pick timestamp, only
/// the parent transaction's, so age is computed against when the transaction was created.</summary>
public sealed record GetPulloutStockAgeAtPickQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<PulloutStockAgeSummaryDto>;

public sealed class GetPulloutStockAgeAtPickHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutStockAgeAtPickQuery, PulloutStockAgeSummaryDto>
{
    public async Task<PulloutStockAgeSummaryDto> Handle(
        GetPulloutStockAgeAtPickQuery request, CancellationToken cancellationToken)
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
                p.PulloutTransactionLine.PulloutTransactionId,
                p.PulloutTransactionLine.PulloutTransaction.ReferenceNumber,
                p.PulloutTransactionLine.PulloutTransaction.CreatedAt,
                p.PulloutTransactionLine.Material.MaterialNumber,
                MaterialDescription = p.PulloutTransactionLine.Material.Description,
                p.PalletPosition.LocationCode,
                p.MfgMonth,
                p.QuantityBoxes
            })
            .ToListAsync(cancellationToken);

        // Age = pick date (parent transaction CreatedAt) minus the manufacturing month, computed
        // in memory to avoid folding date arithmetic into the EF query.
        var rows = picks
            .Select(p => new PulloutStockAgeRowDto(
                p.PulloutTransactionId,
                p.ReferenceNumber,
                p.CreatedAt,
                p.MaterialNumber,
                p.MaterialDescription,
                p.LocationCode,
                MfgMonthParser.Format(p.MfgMonth),
                p.QuantityBoxes,
                (p.CreatedAt.Date - MfgMonthParser.ToDateOnly(p.MfgMonth).ToDateTime(TimeOnly.MinValue)).TotalDays))
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var ages = rows.Select(r => r.AgeDays).OrderBy(a => a).ToList();
        double? average = ages.Count > 0 ? ages.Average() : null;
        double? median = ages.Count switch
        {
            0 => null,
            var n when n % 2 == 1 => ages[n / 2],
            var n => (ages[n / 2 - 1] + ages[n / 2]) / 2.0
        };

        return new PulloutStockAgeSummaryDto(rows.Count, average, median, rows);
    }
}
