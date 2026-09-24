using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inward Reports - "Rejection Reasons" tab: rejected GRNs grouped by
/// <see cref="Domain.Entities.InwardTransaction.RejectionReason"/>, within an optional date range
/// (filtered on <c>RejectedAt</c>).</summary>
public sealed record GetInwardRejectionReasonsQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<InwardRejectionReasonRowDto>>;

public sealed class GetInwardRejectionReasonsHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardRejectionReasonsQuery, IReadOnlyList<InwardRejectionReasonRowDto>>
{
    public async Task<IReadOnlyList<InwardRejectionReasonRowDto>> Handle(
        GetInwardRejectionReasonsQuery request, CancellationToken cancellationToken)
    {
        var query = db.InwardTransactions.AsNoTracking().Where(t => t.IsRejected);

        if (request.FromDate.HasValue)
        {
            query = query.Where(t => t.RejectedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(t => t.RejectedAt <= request.ToDate.Value);
        }

        // Sum-per-transaction is a plain correlated subquery, translatable on its own; grouping by
        // reason then happens in memory (see GetMovementReasonsBreakdown.cs's comment for why a
        // GroupBy combined with an aggregate-into-DTO Select isn't reliably translatable).
        var perGrn = await query
            .Select(t => new { t.RejectionReason, Requested = t.Lines.Sum(l => (decimal?)l.RequestedQuantityBoxes) ?? 0m })
            .ToListAsync(cancellationToken);

        return perGrn
            .GroupBy(t => string.IsNullOrWhiteSpace(t.RejectionReason) ? "(no reason given)" : t.RejectionReason!)
            .Select(g => new InwardRejectionReasonRowDto(g.Key, g.Count(), g.Sum(x => x.Requested)))
            .OrderByDescending(r => r.GrnCount)
            .ToList();
    }
}
