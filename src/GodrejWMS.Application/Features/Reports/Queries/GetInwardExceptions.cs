using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inward Reports - "Exceptions" tab: every inward line whose allocation didn't fully
/// succeed (Partial/Failed), drillable back to its GRN - the row-level detail behind the
/// Dashboard's exception-line count. A plain filtered projection, no grouping, so it doesn't need
/// the anonymous-type-then-in-memory-DTO split the aggregate reports use.</summary>
public sealed record GetInwardExceptionsQuery(
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<IReadOnlyList<InwardExceptionRowDto>>;

public sealed class GetInwardExceptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardExceptionsQuery, IReadOnlyList<InwardExceptionRowDto>>
{
    public async Task<IReadOnlyList<InwardExceptionRowDto>> Handle(
        GetInwardExceptionsQuery request, CancellationToken cancellationToken)
    {
        var query = db.InwardTransactionLines
            .AsNoTracking()
            .Where(l => l.Status != AllocationStatus.Fulfilled);

        if (request.FromDate.HasValue)
        {
            query = query.Where(l => l.InwardTransaction.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(l => l.InwardTransaction.CreatedAt <= request.ToDate.Value);
        }

        var rows = await query
            .OrderByDescending(l => l.InwardTransaction.CreatedAt)
            .Select(l => new
            {
                l.InwardTransactionId,
                l.InwardTransaction.ReferenceNumber,
                l.InwardTransaction.CreatedAt,
                l.Material.MaterialNumber,
                MaterialDescription = l.Material.Description,
                l.MfgMonth,
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                l.Status,
                l.Remarks
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(l => new InwardExceptionRowDto(
                l.InwardTransactionId,
                l.ReferenceNumber,
                l.CreatedAt,
                l.MaterialNumber,
                l.MaterialDescription,
                MfgMonthParser.Format(l.MfgMonth),
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                l.RequestedQuantityBoxes - l.AllocatedQuantityBoxes,
                l.Status.ToString(),
                l.Remarks))
            .ToList();
    }
}
