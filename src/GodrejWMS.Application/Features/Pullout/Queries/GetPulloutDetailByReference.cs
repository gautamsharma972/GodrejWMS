using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

/// <summary>Same lookup as <see cref="GetPulloutDetailQuery"/>, keyed by reference instead of Id -
/// for a multi-sheet workbook upload's per-sheet outcome (<see cref="PulloutSheetImportOutcome"/>),
/// which reports a reference, not an Id, so its "View" / "Download Excel" actions can resolve the
/// pullout without a second round trip through history search.</summary>
public sealed record GetPulloutDetailByReferenceQuery(string ReferenceNumber) : IRequest<PulloutDetailDto>;

public sealed class GetPulloutDetailByReferenceHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutDetailByReferenceQuery, PulloutDetailDto>
{
    public async Task<PulloutDetailDto> Handle(GetPulloutDetailByReferenceQuery request, CancellationToken cancellationToken)
    {
        var id = await db.PulloutTransactions.AsNoTracking()
            .Where(t => t.ReferenceNumber == request.ReferenceNumber)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.ReferenceNumber);

        return await new GetPulloutDetailHandler(db).Handle(new GetPulloutDetailQuery(id), cancellationToken);
    }
}
