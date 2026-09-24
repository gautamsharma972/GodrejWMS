using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inward Reports - "Put-away Turnaround Time" tab: how long each GRN took from receipt
/// to fully confirmed (latest <see cref="Domain.Entities.InwardPutaway.ConfirmedAt"/> across its
/// lines, minus the GRN's own <c>CreatedAt</c>) - the same calc <c>Detail.razor</c> already does
/// per-GRN, here aggregated into a summary plus a sortable per-GRN table. Rejected GRNs are
/// excluded (they were never put away). GRNs with nothing confirmed yet show a null turnaround and
/// don't count toward the average/median.</summary>
public sealed record GetInwardPutawayTurnaroundQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<InwardTurnaroundSummaryDto>;

public sealed class GetInwardPutawayTurnaroundHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardPutawayTurnaroundQuery, InwardTurnaroundSummaryDto>
{
    public async Task<InwardTurnaroundSummaryDto> Handle(
        GetInwardPutawayTurnaroundQuery request, CancellationToken cancellationToken)
    {
        var query = db.InwardTransactions
            .AsNoTracking()
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
            .Where(t => !t.IsRejected);

        if (request.FromDate.HasValue)
        {
            query = query.Where(t => t.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(t => t.CreatedAt <= request.ToDate.Value);
        }

        var transactions = await query.ToListAsync(cancellationToken);

        // Everything from here on is in-memory LINQ-to-Objects (Max over a nested collection plus
        // a DateTimeOffset subtraction), deliberately not folded into the EF query itself.
        var rows = transactions
            .Select(t =>
            {
                var confirmedAt = t.Lines
                    .SelectMany(l => l.Putaways)
                    .Where(p => p.ConfirmedAt.HasValue)
                    .Select(p => p.ConfirmedAt!.Value)
                    .DefaultIfEmpty()
                    .Max();
                var hasConfirmed = confirmedAt != default && t.Lines.All(l =>
                    l.Putaways.All(p => p.IsConfirmed) &&
                    l.Putaways.Where(p => p.IsConfirmed).Sum(p => p.QuantityBoxes) >= l.RequestedQuantityBoxes);

                return new InwardTurnaroundRowDto(
                    t.Id,
                    t.ReferenceNumber,
                    t.CreatedAt,
                    hasConfirmed ? confirmedAt : null,
                    hasConfirmed ? (confirmedAt - t.CreatedAt).TotalHours : null);
            })
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var completedHours = rows.Where(r => r.TurnaroundHours.HasValue).Select(r => r.TurnaroundHours!.Value).OrderBy(h => h).ToList();

        double? average = completedHours.Count > 0 ? completedHours.Average() : null;
        double? median = completedHours.Count switch
        {
            0 => null,
            var n when n % 2 == 1 => completedHours[n / 2],
            var n => (completedHours[n / 2 - 1] + completedHours[n / 2]) / 2.0
        };

        return new InwardTurnaroundSummaryDto(rows.Count, completedHours.Count, average, median, rows);
    }
}
