using GodrejWMS.Application.Common.Permissions;
using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>
/// Rejects a pullout - e.g. the request was raised in error or is no longer needed. A confirmed pullout has already
/// reduced real <see cref="StockBatch"/> inventory, so rejecting it returns those boxes to the locations they were picked from.
/// </summary>
[RequiresPermission(PermissionKeys.Pullout.Reject)]
public sealed record RejectPulloutCommand(string ReferenceNumber, string Reason) : IRequest<Unit>;

public sealed class RejectPulloutValidator : AbstractValidator<RejectPulloutCommand>
{
    public RejectPulloutValidator()
    {
        RuleFor(x => x.ReferenceNumber).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectPulloutHandler(
    IApplicationDbContext db,
    IStockPostingService posting,
    IUnitOfWork uow,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<RejectPulloutCommand, Unit>
{
    public Task<Unit> Handle(RejectPulloutCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<Unit> RunAsync(RejectPulloutCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.PulloutTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.ReferenceNumber == request.ReferenceNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.ReferenceNumber);

        // Reload the header so a long-lived Blazor scope sees a confirmation/rejection made elsewhere.
        await db.Entry(transaction).ReloadAsync(cancellationToken);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This pullout has already been rejected.");
        }

        if (transaction.IsConfirmed)
        {
            // The stock was already taken out (on upload, or at confirmation for an older pullout), so rejecting puts it back into the
            // locations it was picked from - also after the picker confirmed the pick-list. A location that was emptied, or a virtual
            // location that was freed, simply receives the boxes again.
            // PICK-RESERVED put-aways become pending reservations (the user runs Refresh Put-away on the GRN); those boxes are not put back into stock here.
            var skip = await PutawayPickReservation.ReleaseAsync(db, transaction.ReferenceNumber, cancellationToken);
            var returns = new List<StockChange>();
            foreach (var l in transaction.Lines)
            {
                foreach (var p in l.Picks)
                {
                    var key = (l.MaterialId, p.MfgMonth, p.PalletPositionId);
                    var quantity = p.QuantityBoxes;
                    if (skip.TryGetValue(key, out var skipBoxes) && skipBoxes > 0)
                    {
                        var cut = Math.Min(quantity, skipBoxes);
                        quantity -= cut;
                        skip[key] = skipBoxes - cut;
                    }

                    if (quantity > 0)
                    {
                        returns.Add(new StockChange(l.MaterialId, p.MfgMonth, LocationSubtypeIds.Good, p.PalletPosition, quantity));
                    }
                }
            }

            if (returns.Count > 0)
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.Reversal, "PICK", transaction.ReferenceNumber, "Pullout rejected"),
                    returns, cancellationToken);
            }

            transaction.IsConfirmed = false;
        }

        // The proposed/posted lines and picks stay as the audit record of what was rejected.
        transaction.UploadHash = null;
        transaction.IsRejected = true;
        transaction.RejectedAt = clock.UtcNow;
        transaction.RejectedByUserId = currentUser.UserId;
        transaction.RejectedByUserName = currentUser.UserName;
        transaction.RejectionReason = request.Reason.Trim();
        transaction.RowVersion++;

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
