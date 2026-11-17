using GodrejWMS.Application.Common.Permissions;
using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Rejects a GRN that was never (fully) put away - e.g. the physical stock was refused at the
/// dock or the receipt was logged in error. Only possible before any of its put-aways have been
/// confirmed: once a put-away is confirmed it has already written real <see cref="StockBatch"/>
/// inventory, and reversing that is a different problem (a return/reversal transaction), not a
/// rejection of the receipt itself.
/// </summary>
[RequiresPermission(PermissionKeys.Inward.Reject)]
public sealed record RejectInwardCommand(int TransactionId, string Reason) : IRequest<InwardResultDto>;

public sealed class RejectInwardValidator : AbstractValidator<RejectInwardCommand>
{
    public RejectInwardValidator()
    {
        RuleFor(x => x.TransactionId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectInwardHandler(
    IApplicationDbContext db,
    IStockPostingService posting,
    IUnitOfWork uow,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<RejectInwardCommand, InwardResultDto>
{
    public Task<InwardResultDto> Handle(RejectInwardCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<InwardResultDto> RunAsync(RejectInwardCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has already been rejected.");
        }

        // Stock posted on upload is taken back out where it was placed (a picker's relocation is followed). Boxes that were
        // picked, moved or reloaded since can no longer be taken from that cell: the rejection still goes ahead, takes back
        // what is still in place, and records how many boxes could not be reversed.
        // NO RACK put-aways hold no stock (it was cut back when the picker set them aside); GIT ones hold real stock at the GIT location.
        var posted = transaction.Lines
            .SelectMany(l => l.Putaways.Where(p => p.IsConfirmed && (p.ExcludedLocation is null || p.ExcludedLocation == PickerExcludedLocations.Git)).Select(p => (Line: l, Putaway: p)))
            .Where(x => x.Line.MfgMonth is not null)
            .ToList();
        decimal notReversed = 0;
        if (posted.Count > 0)
        {
            var plan = await PostedStockReversal.PlanAsync(
                db,
                posted.Select(x => new StockReversalItem(x.Line.MaterialId, x.Line.MfgMonth!.Value, x.Putaway.PalletPosition, x.Putaway.QuantityBoxes)).ToList(),
                cancellationToken);
            notReversed = plan.ShortfallBoxes;
            if (plan.Changes.Count > 0)
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.Reversal, "GRN", transaction.ReferenceNumber, "GRN rejected"),
                    plan.Changes,
                    cancellationToken);
            }
        }

        // Open (unposted) reservations are simply released so their capacity is free for other allocations. The line-level
        // requested/allocated quantities stay on InwardTransactionLine as the audit record of what was rejected.
        foreach (var line in transaction.Lines)
        {
            foreach (var putaway in line.Putaways.Where(p => !p.IsConfirmed).ToList())
            {
                db.InwardPutaways.Remove(putaway);
            }
        }

        // Frees the upload fingerprint, so a corrected file can be uploaded again.
        transaction.UploadHash = null;

        transaction.IsRejected = true;
        transaction.RejectedAt = clock.UtcNow;
        transaction.RejectedByUserId = currentUser.UserId;
        transaction.RejectedByUserName = currentUser.UserName;
        var reason = request.Reason.Trim();
        if (notReversed > 0)
        {
            var note = $" [{notReversed:0.###} box(es) were no longer at their put-away location (picked/moved) and were not reversed]";
            reason = reason.Length + note.Length > 500 ? reason[..Math.Max(0, 500 - note.Length)] + note : reason + note;
        }

        // Boxes a pullout already holds (PICK-RESERVED) are not reversed here: that pullout needs Refresh Pick to find other stock.
        var reservedRows = transaction.Lines.SelectMany(l => l.Putaways)
            .Where(p => p.ExcludedLocation == PutawayPickReservation.Label && p.OverrideReason is not null && !p.OverrideReason.Contains("released"))
            .ToList();
        if (reservedRows.Count > 0)
        {
            var references = string.Join(", ", reservedRows.Select(p => p.OverrideReason!.Replace("Reserved for pullout ", "")).Distinct());
            var note = $" [{reservedRows.Sum(p => p.QuantityBoxes):0.###} box(es) are PICK-RESERVED for pullout {references} - use Refresh Pick there]";
            reason = reason.Length + note.Length > 500 ? reason[..Math.Max(0, 500 - note.Length)] + note : reason + note;
        }

        transaction.RejectionReason = reason;

        await db.SaveChangesAsync(cancellationToken);

        return InwardResultMapper.ToResult(transaction);
    }
}
