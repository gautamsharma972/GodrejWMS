using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>Counts shown as badges on the Picker's home cards.</summary>
public sealed record PickerPendingCountsDto(int PutawayGrns, int PkmGrns, int PulloutPicklists = 0);

public sealed record GetPickerPendingCountsQuery : IRequest<PickerPendingCountsDto>;

public sealed class GetPickerPendingCountsHandler(IApplicationDbContext db, IPermissionService permissions)
    : IRequestHandler<GetPickerPendingCountsQuery, PickerPendingCountsDto>
{
    public async Task<PickerPendingCountsDto> Handle(GetPickerPendingCountsQuery request, CancellationToken cancellationToken)
    {
        var open = db.InwardTransactions.AsNoTracking().Where(t => !t.IsRejected);
        var inwardReviewer = await permissions.HasPermissionAsync(PermissionKeys.Inward.Confirm, cancellationToken);
        var pulloutReviewer = await permissions.HasPermissionAsync(PermissionKeys.Pullout.Confirm, cancellationToken);

        // A picker: GRNs with a location still to submit. A supervisor: every GRN not confirmed yet.
        var putaway = inwardReviewer
            ? await open.CountAsync(t => t.Lines.Any(l => l.Putaways.Any(p => p.QuantityBoxes > 0 && !p.IsExcludedLocked
                && (!p.IsConfirmed || t.PostedAt != null || p.ExcludedLocation != null))), cancellationToken)
            : await open.CountAsync(t => t.Lines.Any(l => l.Putaways.Any(p => p.QuantityBoxes > 0 && !p.IsExcludedLocked && !p.IsPickerPlaced && p.ExcludedLocation == null
                && (!p.IsConfirmed || t.PostedAt != null))), cancellationToken);

        // Inward (No PKM) GRNs that still have a location waiting for its PKM.
        var pkm = await open
            .CountAsync(t => t.IsPkmless && t.Lines.Any(l => l.MfgMonth == null && l.Putaways.Any(p => !p.IsConfirmed)), cancellationToken);

        // Posted pullouts plus legacy saved drafts that already have allocated picks.
        var picklists = await db.PulloutTransactions.AsNoTracking()
            .CountAsync(t => !t.IsRejected
                && t.Lines.Any(l => l.Picks.Any()) // a pullout with no pick ("0 of 0 picked") is not a task
                && ((t.IsConfirmed && t.UploadHash != null && t.ConfirmedAt == null
                        && (pulloutReviewer || t.Lines.Any(l => l.Picks.Any(p => p.PickedAt == null))))
                    || (!t.IsConfirmed && t.Lines.Any(l => l.Picks.Any()))), cancellationToken);

        return new PickerPendingCountsDto(putaway, pkm, picklists);
    }
}
