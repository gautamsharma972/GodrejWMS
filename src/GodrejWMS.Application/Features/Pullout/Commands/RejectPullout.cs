using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>
/// Rejects a saved pullout that has not been confirmed yet - e.g. the request was raised in error
/// or is no longer needed. Only possible before confirmation: a confirmed pullout has already
/// reduced real <see cref="StockBatch"/> inventory, and undoing that is a different problem (a
/// return/reversal), not a rejection of the request.
/// </summary>
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
            // Posted on upload: the stock was already taken out, so cancelling puts it back - but only while nothing is picked yet.
            if (transaction.Lines.SelectMany(l => l.Picks).Any(p => p.PickedAt is not null))
            {
                throw new InvalidOperationException(
                    "This pullout can't be rejected because picking has already started and the picked stock has left the warehouse.");
            }

            var returns = transaction.Lines
                .SelectMany(l => l.Picks.Select(p => new StockChange(l.MaterialId, p.MfgMonth, LocationSubtypeIds.Good, p.PalletPosition, p.QuantityBoxes)))
                .ToList();
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
