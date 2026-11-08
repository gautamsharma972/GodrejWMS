using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>The picker's final Confirm of a pullout posted on upload, once every pick has been confirmed (or changed and confirmed).
/// It sets the pick reference to confirmed and takes it off the picker's list; the pullout can no longer be cancelled afterwards.</summary>
public sealed record ConfirmPulloutPicklistCommand(int PulloutId) : IRequest<ConfirmPulloutPicklistResult>;

public sealed record ConfirmPulloutPicklistResult(string ReferenceNumber, int PickCount, decimal PickedBoxes);

public sealed class ConfirmPulloutPicklistValidator : AbstractValidator<ConfirmPulloutPicklistCommand>
{
    public ConfirmPulloutPicklistValidator() => RuleFor(x => x.PulloutId).GreaterThan(0);
}

public sealed class ConfirmPulloutPicklistHandler(
    IApplicationDbContext db, IPermissionService permissions, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<ConfirmPulloutPicklistCommand, ConfirmPulloutPicklistResult>
{
    public async Task<ConfirmPulloutPicklistResult> Handle(ConfirmPulloutPicklistCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Pullout.Pick, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to confirm pick-lists.");
        }

        var transaction = await db.PulloutTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Picks)
            .FirstOrDefaultAsync(t => t.Id == request.PulloutId, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.PulloutId);
        await db.Entry(transaction).ReloadAsync(cancellationToken);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This pullout has been rejected.");
        }

        if (!transaction.IsConfirmed || transaction.UploadHash is null)
        {
            throw new InvalidOperationException("This pullout was not posted on upload; a supervisor confirms it.");
        }

        if (transaction.ConfirmedAt is not null)
        {
            throw new InvalidOperationException("This pick-list has already been confirmed.");
        }

        var picks = transaction.Lines.SelectMany(l => l.Picks).ToList();
        // The long-lived Blazor context may hold stale copies of picks another session just confirmed.
        foreach (var pick in picks)
        {
            await db.Entry(pick).ReloadAsync(cancellationToken);
        }

        var open = picks.Count(p => p.PickedAt is null);
        if (open > 0)
        {
            throw new InvalidOperationException($"Confirm every pick first - {open} pick(s) of this pick-list have not been confirmed yet.");
        }

        transaction.ConfirmedAt = clock.UtcNow;
        transaction.ConfirmedByUserId = currentUser.UserId;
        transaction.ConfirmedByUserName = currentUser.UserName;
        transaction.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return new ConfirmPulloutPicklistResult(transaction.ReferenceNumber, picks.Count, picks.Sum(p => p.PickedQuantityBoxes ?? p.QuantityBoxes));
    }
}
