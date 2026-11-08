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

/// <summary>The picker's final Confirm of a pullout posted on upload, once every pick has been confirmed. Only now does stock change for
/// what the picker decided: boxes not found go back to stock and the remainders the picker chose to move are moved, all in one
/// transaction. It sets the pick reference to confirmed, takes it off the picker's list, and the picks can no longer be edited.</summary>
public sealed record ConfirmPulloutPicklistCommand(int PulloutId) : IRequest<ConfirmPulloutPicklistResult>;

public sealed record ConfirmPulloutPicklistResult(string ReferenceNumber, int PickCount, decimal PickedBoxes, decimal MovedBoxes);

public sealed class ConfirmPulloutPicklistValidator : AbstractValidator<ConfirmPulloutPicklistCommand>
{
    public ConfirmPulloutPicklistValidator() => RuleFor(x => x.PulloutId).GreaterThan(0);
}

public sealed class ConfirmPulloutPicklistHandler(
    IApplicationDbContext db,
    IStockPostingService posting,
    IUnitOfWork uow,
    IPermissionService permissions,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<ConfirmPulloutPicklistCommand, ConfirmPulloutPicklistResult>
{
    public async Task<ConfirmPulloutPicklistResult> Handle(ConfirmPulloutPicklistCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Pullout.Pick, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to confirm pick-lists.");
        }

        return await InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);
    }

    private async Task<ConfirmPulloutPicklistResult> RunAsync(ConfirmPulloutPicklistCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.PulloutTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.PalletPosition).ThenInclude(p => p.Rack)
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.MoveToPalletPosition).ThenInclude(p => p!.Rack)
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

        var picks = transaction.Lines.SelectMany(l => l.Picks.Select(p => (Line: l, Pick: p))).ToList();
        // The long-lived Blazor context may hold stale copies of picks another session just confirmed.
        foreach (var (_, pick) in picks)
        {
            await db.Entry(pick).ReloadAsync(cancellationToken);
        }

        var open = picks.Count(x => x.Pick.PickedAt is null);
        if (open > 0)
        {
            throw new InvalidOperationException($"Confirm every pick first - {open} pick(s) of this pick-list have not been confirmed yet.");
        }

        // 1. The picker may have picked fewer or more boxes than allocated. Boxes not found were already taken out of stock at upload:
        //    they go back where they were. Boxes picked beyond the allocation are taken from what is still on the shelf there.
        var adjustments = new List<StockChange>();
        foreach (var (line, pick) in picks)
        {
            var picked = pick.PickedQuantityBoxes ?? pick.QuantityBoxes;
            var difference = pick.QuantityBoxes - picked; // positive: shortfall to hand back, negative: extra boxes to take
            if (difference == 0)
            {
                continue;
            }

            adjustments.Add(new StockChange(line.MaterialId, pick.MfgMonth, LocationSubtypeIds.Good, pick.PalletPosition, difference));
            pick.QuantityBoxes = picked;
            line.PickedQuantityBoxes -= difference;
            line.Status = line.PickedQuantityBoxes >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : line.PickedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
            line.Remarks = line.Status == AllocationStatus.Fulfilled
                ? null
                : $"Picker picked {line.PickedQuantityBoxes:0.###} of {line.RequestedQuantityBoxes:0.###} box(es).";
        }

        if (adjustments.Count > 0)
        {
            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.PickChange, "PICK", transaction.ReferenceNumber, "Picked quantity differs from the allocation"),
                adjustments, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        // 2. The remainders the picker chose to move, validated against the stock as it is now (one at a time, so two moves into the
        //    same location are checked together).
        var moved = 0m;
        foreach (var (line, pick) in picks.Where(x => x.Pick is { MoveToPalletPositionId: not null, MoveQuantityBoxes: > 0 }))
        {
            var quantity = pick.MoveQuantityBoxes!.Value;
            var source = pick.PalletPosition;
            var destination = pick.MoveToPalletPosition!;
            var onHand = await db.StockBatches.AsNoTracking()
                .Where(b => b.PalletPositionId == source.Id && b.MaterialId == line.MaterialId && b.MfgMonth == pick.MfgMonth)
                .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
            if (quantity > onHand)
            {
                throw new InvalidOperationException(
                    $"Only {onHand:0.###} box(es) remain at {source.LocationCode} now; the {quantity:0.###} you chose to move to {destination.LocationCode} are not all there. Edit the pick and confirm again.");
            }

            var problem = await RelocationRules.ValidateDestinationAsync(
                db, destination, line.MaterialId, source.Id, transaction.WarehouseId, quantity, cancellationToken);
            if (problem is not null)
            {
                throw new InvalidOperationException($"{problem} Edit the pick and choose another location.");
            }

            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.PickRelocate, "PICK", transaction.ReferenceNumber, $"Remainder moved from {source.LocationCode}"),
                [
                    new StockChange(line.MaterialId, pick.MfgMonth, LocationSubtypeIds.Good, source, -quantity),
                    new StockChange(line.MaterialId, pick.MfgMonth, LocationSubtypeIds.Good, destination, quantity)
                ],
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            moved += quantity;
        }

        transaction.ConfirmedAt = clock.UtcNow;
        transaction.ConfirmedByUserId = currentUser.UserId;
        transaction.ConfirmedByUserName = currentUser.UserName;
        transaction.RowVersion++;

        // The picks are done: a virtual location that is now empty is free for reuse.
        await posting.ReleaseEmptyVirtualLocationsAsync(picks.Select(x => x.Pick.PalletPosition), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new ConfirmPulloutPicklistResult(
            transaction.ReferenceNumber, picks.Count, picks.Sum(x => x.Pick.PickedQuantityBoxes ?? x.Pick.QuantityBoxes), moved);
    }
}
