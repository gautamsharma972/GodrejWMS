using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Where the boxes left at the pick location after a partial pick go (e.g. 45 of 50 picked from a top-level bin, the other
/// 5 moved to a ground-level location).</summary>
public sealed record PulloutRemainderMove(int DestinationPalletPositionId, decimal QuantityBoxes);

/// <summary>The Picker's physical confirmation of one pick of a posted pullout: the quantity really picked (never more than
/// allocated; a shortfall goes back to stock) and, optionally, where the remainder of that location's stock moves to. Pick
/// confirmation and the remainder move commit in ONE transaction.</summary>
public sealed record ConfirmPulloutPickCommand(int PickId, decimal PickedQuantityBoxes, PulloutRemainderMove? MoveRemaining = null)
    : IRequest<ConfirmPulloutPickResult>;

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
    IStockPostingService posting,
    IUnitOfWork uow,
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

        return await InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);
    }

    private async Task<ConfirmPulloutPickResult> RunAsync(ConfirmPulloutPickCommand request, CancellationToken cancellationToken)
    {
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

        if (pick.PickedAt is not null)
        {
            throw new InvalidOperationException($"The pick at {pick.PalletPosition.LocationCode} has already been confirmed.");
        }

        var planned = pick.QuantityBoxes;
        if (request.PickedQuantityBoxes > planned)
        {
            throw new InvalidOperationException(
                $"Only {planned:0.###} box(es) are allocated for this pick; {request.PickedQuantityBoxes:0.###} cannot be picked.");
        }

        var source = pick.PalletPosition;
        var shortfall = planned - request.PickedQuantityBoxes;
        var now = clock.UtcNow;

        // Boxes the Picker did not find were already taken out of stock at upload: they go back where they were.
        if (shortfall > 0)
        {
            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.PickChange, "PICK", transaction.ReferenceNumber, "Short pick - returned to stock"),
                [new StockChange(line.MaterialId, pick.MfgMonth, LocationSubtypeIds.Good, source, shortfall)],
                cancellationToken);
            pick.QuantityBoxes = request.PickedQuantityBoxes;
            line.PickedQuantityBoxes -= shortfall;
            line.Status = line.PickedQuantityBoxes >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : line.PickedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
            line.Remarks = line.Status == AllocationStatus.Fulfilled
                ? null
                : $"Picker picked {line.PickedQuantityBoxes:0.###} of {line.RequestedQuantityBoxes:0.###} box(es).";
        }

        pick.PickedAt = now;
        pick.PickedByUserId = currentUser.UserId;
        pick.PickedByUserName = currentUser.UserName;
        pick.PickedQuantityBoxes = request.PickedQuantityBoxes;

        // What is still on the shelf at the pick location for this material + PKM.
        var remaining = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == source.Id && b.MaterialId == line.MaterialId && b.MfgMonth == pick.MfgMonth)
            .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        remaining += shortfall; // the return above is not saved yet

        var moved = 0m;
        string? destinationCode = null;
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

            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.PickRelocate, "PICK", transaction.ReferenceNumber, $"Remainder moved from {source.LocationCode}"),
                [
                    new StockChange(line.MaterialId, pick.MfgMonth, LocationSubtypeIds.Good, source, -move.QuantityBoxes),
                    new StockChange(line.MaterialId, pick.MfgMonth, LocationSubtypeIds.Good, destination!, move.QuantityBoxes)
                ],
                cancellationToken);
            moved = move.QuantityBoxes;
            destinationCode = destination!.LocationCode;
            remaining -= moved;
        }

        transaction.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return new ConfirmPulloutPickResult(
            transaction.ReferenceNumber, request.PickedQuantityBoxes, shortfall, moved, remaining, destinationCode);
    }
}
