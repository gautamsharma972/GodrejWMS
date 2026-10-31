using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>Same lookup as <see cref="GetInwardDetailQuery"/>, keyed by GRN reference instead of
/// Id - for places that only have the reference on hand, such as a multi-sheet workbook upload's
/// per-sheet outcome (<see cref="InwardSheetImportOutcome"/>), which reports a reference, not an
/// Id, so its "View" / "Download Excel" actions can resolve the GRN without a second round trip
/// through history search.</summary>
public sealed record GetInwardDetailByReferenceQuery(string ReferenceNumber) : IRequest<InwardDetailDto>;

public sealed class GetInwardDetailByReferenceHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardDetailByReferenceQuery, InwardDetailDto>
{
    public async Task<InwardDetailDto> Handle(GetInwardDetailByReferenceQuery request, CancellationToken cancellationToken)
    {
        var id = await db.InwardTransactions.AsNoTracking()
            .Where(t => t.ReferenceNumber == request.ReferenceNumber)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.ReferenceNumber);

        return await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(id), cancellationToken);
    }
}
