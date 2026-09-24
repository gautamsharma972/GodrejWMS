using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inventory Movement Reports - "Reasons Breakdown" tab: count/quantity of completed
/// movements grouped by <see cref="Domain.Entities.MovementReason"/>, within an optional date range.</summary>
public sealed record GetMovementReasonsBreakdownQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<MovementReasonBreakdownRowDto>>;

public sealed class GetMovementReasonsBreakdownHandler(IApplicationDbContext db)
    : IRequestHandler<GetMovementReasonsBreakdownQuery, IReadOnlyList<MovementReasonBreakdownRowDto>>
{
    public async Task<IReadOnlyList<MovementReasonBreakdownRowDto>> Handle(
        GetMovementReasonsBreakdownQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockMovements.AsNoTracking().AsQueryable();

        if (request.FromDate.HasValue)
        {
            query = query.Where(m => m.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(m => m.CreatedAt <= request.ToDate.Value);
        }

        // Grouped/aggregated server-side into a plain anonymous type, then shaped into the DTO as
        // a separate in-memory step - constructing the DTO's positional-record inline inside the
        // EF query (GroupBy -> Select(new Dto(...))) doesn't translate on every provider (confirmed
        // via a unit test against EF Core InMemory; the untested GetStockSummary.cs uses the same
        // inline-constructor shape and presumably only ever ran against real MySQL, which is more
        // permissive here).
        var grouped = await query
            .GroupBy(m => new { m.ReasonId, m.Reason.Code, m.Reason.DisplayName })
            .Select(g => new { g.Key.ReasonId, g.Key.Code, g.Key.DisplayName, Count = g.Count(), Total = g.Sum(m => m.QuantityBoxes) })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(g => new MovementReasonBreakdownRowDto(g.ReasonId, g.Code, g.DisplayName, g.Count, g.Total))
            .OrderByDescending(r => r.MovementCount)
            .ToList();
    }
}
