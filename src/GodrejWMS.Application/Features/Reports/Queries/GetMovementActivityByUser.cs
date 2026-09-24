using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inventory Movement Reports - "Activity by User" tab: count/quantity of completed
/// movements grouped by the performing user, within an optional date range.</summary>
public sealed record GetMovementActivityByUserQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<MovementUserActivityRowDto>>;

public sealed class GetMovementActivityByUserHandler(IApplicationDbContext db)
    : IRequestHandler<GetMovementActivityByUserQuery, IReadOnlyList<MovementUserActivityRowDto>>
{
    public async Task<IReadOnlyList<MovementUserActivityRowDto>> Handle(
        GetMovementActivityByUserQuery request, CancellationToken cancellationToken)
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

        // See the identical comment in GetMovementReasonsBreakdown.cs - the DTO is constructed as
        // a separate in-memory step after the aggregate query, not inline inside it.
        var grouped = await query
            .GroupBy(m => m.PerformedByUserName ?? "Unknown user")
            .Select(g => new { UserName = g.Key, Count = g.Count(), Total = g.Sum(m => m.QuantityBoxes) })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(g => new MovementUserActivityRowDto(g.UserName, g.Count, g.Total))
            .OrderByDescending(r => r.MovementCount)
            .ToList();
    }
}
