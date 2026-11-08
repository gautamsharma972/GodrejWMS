using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Where the boxes left at the pick location after a partial pick should go (e.g. 45 of 50 picked from a top-level bin, the
/// other 5 moved to a ground-level location). Only the choice is recorded with the pick: nothing moves until the picker confirms the
/// whole pick-list, and it is optional.</summary>
public sealed record PulloutRemainderMove(int DestinationPalletPositionId, decimal QuantityBoxes);

/// <summary>The Picker's confirmation of one pick of a posted pullout: the quantity really picked (never more than allocated) and,
/// optionally, where the boxes left at that location should go. It can be changed again until the pick-list is confirmed. Stock only
/// changes when the pick-list is confirmed (<see cref="ConfirmPulloutPicklistCommand"/>): a shortfall goes back to stock and the chosen
/// remainder move is carried out then.</summary>
public sealed record ConfirmPulloutPickCommand(int PickId, decimal PickedQuantityBoxes, PulloutRemainderMove? MoveRemaining = null)
    : IRequest<ConfirmPulloutPickResult>;

/// <param name="ReturnedToStockBoxes">Boxes not found, which go back to stock when the pick-list is confirmed.</param>
/// <param name="MovedBoxes">Boxes that will move to <paramref name="DestinationLocationCode"/> when the pick-list is confirmed.</param>
public sealed record ConfirmPulloutPickResult(
    string ReferenceNumber,
    decimal PickedBoxes,
    decimal ReturnedToStockBoxes,
    decimal MovedBoxes,
    decimal RemainingAtSourceBoxes,
    string? DestinationLocationCode);

public sealed class ConfirmPulloutPickValidator : AbstractValidator<ConfirmPulloutPickCommand>
{
    public ConfirmPulloutPickValidator()
    {
        RuleFor(x => x.PickId).GreaterThan(0);
        RuleFor(x => x.PickedQuantityBoxes).GreaterThan(0).WithMessage("Picked quantity must be greater than zero.");
        When(x => x.MoveRemaining is not null, () =>
        {
            RuleFor(x => x.MoveRemaining!.DestinationPalletPositionId).GreaterThan(0).WithMessage("Select the location to move the remaining boxes to.");
            RuleFor(x => x.MoveRemaining!.QuantityBoxes).GreaterThan(0).WithMessage("The quantity to move must be greater than zero.");
        });
    }
}

public sealed class ConfirmPulloutPickHandler(
    IApplicationDbContext db,
    IPermissionService permissions,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<ConfirmPulloutPickCommand, ConfirmPulloutPickResult>
{
    public async Task<ConfirmPulloutPickResult> Handle(ConfirmPulloutPickCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Pullout.Pick, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to confirm picks.");
        }

        var pick = await db.PulloutPicks
            .Include(p => p.PalletPosition).ThenInclude(p => p.Rack)
            .Include(p => p.PulloutTransactionLine).ThenInclude(l => l.PulloutTransaction)
            .FirstOrDefaultAsync(p => p.Id == request.PickId, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutPick), request.PickId);
        var line = pick.PulloutTransactionLine;
        var transaction = line.PulloutTransaction;
        await db.Entry(transaction).ReloadAsync(cancellationToken);
        await db.Entry(pick).ReloadAsync(cancellationToken);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This pullout has been rejected.");
        }

        if (!transaction.IsConfirmed)
        {
            throw new InvalidOperationException("This pullout has not been posted yet; confirm it from the supervisor screen.");
        }

        if (transaction.ConfirmedAt is not null || transaction.UploadHash is null)
        {
            throw new InvalidOperationException("This pick-list has already been confirmed, so its picks can no longer be changed.");
        }

        var planned = pick.QuantityBoxes;
        if (request.PickedQuantityBoxes > planned)
        {
            throw new InvalidOperationException(
                $"Only {planned:0.###} box(es) are allocated for this pick; {request.PickedQuantityBoxes:0.###} cannot be picked.");
        }

        var source = pick.PalletPosition;
        var shortfall = planned - request.PickedQuantityBoxes;

        // What will be left on the shelf at the pick location for this material + PKM once the pick-list is confirmed.
        var onHand = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == source.Id && b.MaterialId == line.MaterialId && b.MfgMonth == pick.MfgMonth)
            .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        var remaining = onHand + shortfall;

        var moved = 0m;
        string? destinationCode = null;
        int? destinationId = null;
        if (request.MoveRemaining is { } move)
        {
            if (move.QuantityBoxes > remaining)
            {
                throw new InvalidOperationException(
                    $"Only {remaining:0.###} box(es) remain at {source.LocationCode}; {move.QuantityBoxes:0.###} cannot be moved.");
            }

            var destination = await db.PalletPositions.Include(p => p.Rack)
                .FirstOrDefaultAsync(p => p.Id == move.DestinationPalletPositionId, cancellationToken);
            var problem = await RelocationRules.ValidateDestinationAsync(
                db, destination, line.MaterialId, source.Id, transaction.WarehouseId, move.QuantityBoxes, cancellationToken);
            if (problem is not null)
            {
                throw new InvalidOperationException(problem);
            }

            moved = move.QuantityBoxes;
            destinationCode = destination!.LocationCode;
            destinationId = destination.Id;
        }

        // Only the picker's decision is recorded; stock does not change until the pick-list is confirmed.
        pick.PickedAt = clock.UtcNow;
        pick.PickedByUserId = currentUser.UserId;
        pick.PickedByUserName = currentUser.UserName;
        pick.PickedQuantityBoxes = request.PickedQuantityBoxes;
        pick.MoveToPalletPositionId = destinationId;
        pick.MoveQuantityBoxes = destinationId is null ? null : moved;

        transaction.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return new ConfirmPulloutPickResult(
            transaction.ReferenceNumber, request.PickedQuantityBoxes, shortfall, moved, remaining - moved, destinationCode);
    }
}
