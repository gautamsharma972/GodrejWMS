using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Pullout Reports - "Exceptions" tab: every pullout line whose pick didn't fully succeed
/// (Partial/Failed), drillable back to its transaction. Mirrors GetInwardExceptions.cs for the
/// outbound side.</summary>
public sealed record GetPulloutExceptionsQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<PulloutExceptionRowDto>>;

public sealed class GetPulloutExceptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutExceptionsQuery, IReadOnlyList<PulloutExceptionRowDto>>
{
    public async Task<IReadOnlyList<PulloutExceptionRowDto>> Handle(
        GetPulloutExceptionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PulloutTransactionLines
            .AsNoTracking()
            .Where(l => l.PulloutTransaction.IsConfirmed && l.Status != AllocationStatus.Fulfilled);

        if (request.FromDate.HasValue)
        {
            query = query.Where(l => l.PulloutTransaction.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(l => l.PulloutTransaction.CreatedAt <= request.ToDate.Value);
        }

        var rows = await query
            .OrderByDescending(l => l.PulloutTransaction.CreatedAt)
            .Select(l => new
            {
                l.PulloutTransactionId,
                l.PulloutTransaction.ReferenceNumber,
                l.PulloutTransaction.CreatedAt,
                l.Material.MaterialNumber,
                MaterialDescription = l.Material.Description,
                l.RequestedQuantityBoxes,
                l.PickedQuantityBoxes,
                l.Status,
                l.Remarks
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(l => new PulloutExceptionRowDto(
                l.PulloutTransactionId,
                l.ReferenceNumber,
                l.CreatedAt,
                l.MaterialNumber,
                l.MaterialDescription,
                l.RequestedQuantityBoxes,
                l.PickedQuantityBoxes,
                l.RequestedQuantityBoxes - l.PickedQuantityBoxes,
                l.Status.ToString(),
                l.Remarks))
            .ToList();
    }
}
