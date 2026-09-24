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
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<RejectPulloutCommand, Unit>
{
    public async Task<Unit> Handle(RejectPulloutCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.PulloutTransactions
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
            throw new InvalidOperationException(
                "This pullout can't be rejected because it has already been confirmed and inventory was updated.");
        }

        // A saved pullout is only a proposal (stock isn't reserved), so rejecting it just records
        // the decision; the proposed lines and picks stay as the audit record of what was rejected.
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
