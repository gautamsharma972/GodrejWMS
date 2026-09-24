using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inventory Movement Reports - "Location Churn" tab: how often each location was a
/// source or destination of a completed movement, within an optional date range - surfaces
/// high-traffic or problem locations. Source and destination are grouped separately (EF can't
/// cleanly union two differently-keyed GroupBys in one query) then merged in memory; the result
/// set is bounded by the warehouse's location count, not movement volume.</summary>
public sealed record GetMovementLocationChurnQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<MovementLocationChurnRowDto>>;

public sealed class GetMovementLocationChurnHandler(IApplicationDbContext db)
    : IRequestHandler<GetMovementLocationChurnQuery, IReadOnlyList<MovementLocationChurnRowDto>>
{
    public async Task<IReadOnlyList<MovementLocationChurnRowDto>> Handle(
        GetMovementLocationChurnQuery request, CancellationToken cancellationToken)
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

        var outbound = await query
            .GroupBy(m => m.SourcePalletPosition.LocationCode)
            .Select(g => new { LocationCode = g.Key, Count = g.Count(), Quantity = g.Sum(m => m.QuantityBoxes) })
            .ToListAsync(cancellationToken);

        var inbound = await query
            .GroupBy(m => m.DestinationPalletPosition.LocationCode)
            .Select(g => new { LocationCode = g.Key, Count = g.Count(), Quantity = g.Sum(m => m.QuantityBoxes) })
            .ToListAsync(cancellationToken);

        var locationCodes = outbound.Select(o => o.LocationCode)
            .Union(inbound.Select(i => i.LocationCode))
            .ToList();

        var rows = locationCodes
            .Select(code =>
            {
                var outRow = outbound.FirstOrDefault(o => o.LocationCode == code);
                var inRow = inbound.FirstOrDefault(i => i.LocationCode == code);
                var movesOut = outRow?.Count ?? 0;
                var movesIn = inRow?.Count ?? 0;

                return new MovementLocationChurnRowDto(
                    code,
                    movesOut,
                    movesIn,
                    movesOut + movesIn,
                    outRow?.Quantity ?? 0,
                    inRow?.Quantity ?? 0);
            })
            .OrderByDescending(r => r.TotalMoves)
            .ToList();

        return rows;
    }
}
