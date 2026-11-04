using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>Counts shown as badges on the Picker's home cards.</summary>
public sealed record PickerPendingCountsDto(int PutawayGrns, int PkmGrns);

public sealed record GetPickerPendingCountsQuery : IRequest<PickerPendingCountsDto>;

public sealed class GetPickerPendingCountsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPickerPendingCountsQuery, PickerPendingCountsDto>
{
    public async Task<PickerPendingCountsDto> Handle(GetPickerPendingCountsQuery request, CancellationToken cancellationToken)
    {
        var open = db.InwardTransactions.AsNoTracking().Where(t => !t.IsRejected);

        // GRNs with a location the picker has not submitted yet (a GIT / NO RACK entry or a saved location counts as submitted).
        var putaway = await open
            .CountAsync(t => t.Lines.Any(l => l.Putaways.Any(p => !p.IsConfirmed && !p.IsPickerPlaced)), cancellationToken);

        // Inward (No PKM) GRNs that still have a location waiting for its PKM.
        var pkm = await open
            .CountAsync(t => t.IsPkmless && t.Lines.Any(l => l.MfgMonth == null && l.Putaways.Any(p => !p.IsConfirmed)), cancellationToken);

        return new PickerPendingCountsDto(putaway, pkm);
    }
}
