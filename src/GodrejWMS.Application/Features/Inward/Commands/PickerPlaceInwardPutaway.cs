using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>The Picker's Put-away screen: records where one reserved location's goods really went, the quantity actually
/// found and its PKM. <see cref="NewPalletPositionId"/> is a real location; <see cref="ExcludedLocation"/> ("GIT" or
/// "NO RACK") sets the goods aside instead - that put-away is closed without ever creating stock. When the target
/// location already holds confirmed stock or another GRN's reservation, nothing is saved the first time and a warning is
/// returned; sending the same command with <see cref="AcknowledgeWarning"/> saves it anyway.</summary>
public sealed record PickerPlaceInwardPutawayCommand(
    int PutawayId,
    decimal QuantityBoxes,
    string? MfgMonthText,
    int? NewPalletPositionId,
    string? ExcludedLocation,
    bool AcknowledgeWarning = false) : IRequest<PickerPlaceInwardPutawayResult>;

public sealed record PickerPlaceInwardPutawayResult(bool Saved, string? Warning);

public static class PickerExcludedLocations
{
    public const string Git = "GIT";
    public const string NoRack = "NO RACK";

    public static string? Normalize(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        return text switch
        {
            Git => Git,
            NoRack => NoRack,
            _ => null
        };
    }
}

public sealed class PickerPlaceInwardPutawayValidator : AbstractValidator<PickerPlaceInwardPutawayCommand>
{
    public PickerPlaceInwardPutawayValidator()
    {
        RuleFor(x => x.PutawayId).GreaterThan(0);
        RuleFor(x => x.QuantityBoxes).GreaterThan(0).WithMessage("Qty must be greater than zero.");
        RuleFor(x => x).Must(x => x.NewPalletPositionId is > 0 || PickerExcludedLocations.Normalize(x.ExcludedLocation) is not null)
            .WithMessage("Select the new location, GIT or NO RACK.");
        RuleFor(x => x.MfgMonthText!)
            .Must(text => MfgMonthParser.TryParse(text, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.MfgMonthText))
            .WithMessage("PKM must look like 'MAR|2026'.");
    }
}

public sealed class PickerPlaceInwardPutawayHandler(
    IApplicationDbContext db, IStockPostingService posting, IGitLocationService gitLocations, IUnitOfWork uow,
    IPermissionService permissions, ICurrentUserService currentUser, IDateTimeProvider clock)
    : IRequestHandler<PickerPlaceInwardPutawayCommand, PickerPlaceInwardPutawayResult>
{
    public async Task<PickerPlaceInwardPutawayResult> Handle(PickerPlaceInwardPutawayCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inward.AssignPkm, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to update put-away locations.");
        }

        return await InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);
    }

    private async Task<PickerPlaceInwardPutawayResult> RunAsync(PickerPlaceInwardPutawayCommand request, CancellationToken cancellationToken)
    {
        var putaway = await db.InwardPutaways
            .Include(p => p.PalletPosition)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.Material)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.InwardTransaction)
                .ThenInclude(t => t.Lines).ThenInclude(l => l.Putaways)
            .FirstOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);

        // The circuit's DbContext is long-lived: re-read the rows this decision depends on.
        await db.Entry(putaway).ReloadAsync(cancellationToken);

        var line = putaway.InwardTransactionLine;
        var transaction = line.InwardTransaction;
        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected.");
        }

        // Stock posted on upload sits at the suggested location until the picker confirms or changes it; an order still pending
        // from before posting on upload only has a reservation, which a supervisor confirms later.
        var postedOrder = transaction.PostedAt is not null;
        if (putaway.IsConfirmed && putaway.ExcludedLocation is null && !postedOrder)
        {
            throw new InvalidOperationException($"{putaway.PalletPosition.LocationCode} is already confirmed and can no longer be changed.");
        }

        // A GIT / NO RACK entry can be corrected until a supervisor confirms the GRN, or it is rejected.
        if (putaway.IsExcludedLocked)
        {
            throw new InvalidOperationException("This GRN has already been confirmed, so its locations, PKM and GIT / NO RACK entries can no longer be changed.");
        }

        var excluded = PickerExcludedLocations.Normalize(request.ExcludedLocation);
        PalletPosition? target = null;
        if (excluded is null)
        {
            target = await db.PalletPositions
                .Include(p => p.Rack)
                .FirstOrDefaultAsync(p => p.Id == request.NewPalletPositionId, cancellationToken)
                ?? throw new InvalidOperationException("The selected location was not found.");
            if (!target.IsActive || !target.Rack.IsActive)
            {
                throw new InvalidOperationException($"Location {target.LocationCode} is not active.");
            }

            if (target.Rack.WarehouseId != transaction.WarehouseId)
            {
                throw new InvalidOperationException($"Location {target.LocationCode} belongs to a different warehouse.");
            }
        }

        // GIT is a real, trackable location (unlike NO RACK, which still has no destination at all): the goods
        // physically go into the warehouse's one GIT staging location instead of a picker-chosen shelf.
        var gitPosition = excluded == PickerExcludedLocations.Git
            ? await gitLocations.GetOrCreateAsync(transaction.WarehouseId, cancellationToken)
            : null;
        var newPosition = excluded == PickerExcludedLocations.NoRack ? null : target ?? gitPosition;

        // PKM: the one entered here, else the line's own. Required whenever the goods go somewhere trackable
        // (a real location or GIT) - only NO RACK has no stock to key by PKM at all.
        int? pkm = line.MfgMonth;
        if (!string.IsNullOrWhiteSpace(request.MfgMonthText))
        {
            MfgMonthParser.TryParse(request.MfgMonthText, out var parsed);
            pkm = parsed;
        }

        if (excluded != PickerExcludedLocations.NoRack && pkm is null)
        {
            throw new InvalidOperationException("Select the PKM for this location.");
        }

        if (target is not null && !request.AcknowledgeWarning)
        {
            var warning = await BuildWarningAsync(putaway, line, target, cancellationToken);
            if (warning is not null)
            {
                return new PickerPlaceInwardPutawayResult(false, warning);
            }
        }

        // Quantity actually put away: only the line's allocated total follows it. The requested quantity is the inward
        // order's own figure (the GRN's inbound volume) and is never changed by the picker.
        var originalQuantity = putaway.QuantityBoxes;
        var hadStock = postedOrder && putaway.IsConfirmed && putaway.ExcludedLocation != PickerExcludedLocations.NoRack && line.MfgMonth is not null;
        var oldPkm = line.MfgMonth;
        var oldStockPosition = putaway.PalletPosition;
        // Remembered so the supervisor's Confirm can check the entries still add up to what the system allocated.
        putaway.SuggestedQuantityBoxes ??= originalQuantity;
        putaway.QuantityBoxes = request.QuantityBoxes;
        line.AllocatedQuantityBoxes += request.QuantityBoxes - originalQuantity;

        // PKM: the put-away belongs to the line carrying its PKM (stock is keyed by line PKM), so it moves to the
        // GRN's line for this material + PKM (created when none exists). A lone no-PKM line just takes the PKM.
        if (excluded != PickerExcludedLocations.NoRack && pkm != line.MfgMonth)
        {
            MovePutawayToPkmLine(transaction, line, putaway, originalQuantity, pkm!.Value);
        }

        foreach (var touched in transaction.Lines.Where(l => l == line || l.Putaways.Any(p => p == putaway)).ToList())
        {
            RefreshLineStatus(touched);
        }

        // Posted stock follows the picker's decision through the shared posting service: out of the old cell, into the new one
        // (a GIT / NO RACK entry takes the stock out of inventory; correcting it back to a real location puts it in again).
        if (postedOrder)
        {
            var changes = new List<StockChange>();
            var removeQuantity = originalQuantity;
            var addQuantity = request.QuantityBoxes;
            var returnedToOldCell = 0m;
            var repointedToNewCell = 0m;
            var noMove = hadStock && newPosition is not null && newPosition.Id == oldStockPosition.Id && pkm == oldPkm && request.QuantityBoxes == originalQuantity;
            if (hadStock && !noMove)
            {
                // Stock is available for picking as soon as it is posted, so a pullout may already have taken boxes of this put-away
                // out of the suggested location. Those boxes are not on the shelf to move: the pullout's unconfirmed picks of the
                // cell follow the stock to the new location (or are cut back when the boxes are set aside as NO RACK).
                (removeQuantity, addQuantity, returnedToOldCell, repointedToNewCell) = await FollowOpenPicksAsync(
                    line, oldStockPosition, oldPkm!.Value, originalQuantity, newPosition, pkm, request.QuantityBoxes, excluded, cancellationToken);
            }

            if (hadStock)
            {
                changes.Add(new StockChange(line.MaterialId, oldPkm!.Value, LocationSubtypeIds.Good, oldStockPosition, -removeQuantity + returnedToOldCell));
            }

            if (newPosition is not null)
            {
                changes.Add(new StockChange(line.MaterialId, pkm!.Value, LocationSubtypeIds.Good, newPosition, addQuantity - repointedToNewCell));
            }

            if (changes.Count > 0)
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.PutawayChange, "GRN", transaction.ReferenceNumber,
                        excluded is not null ? $"Set aside as {excluded} by picker" : "Location set by picker"),
                    changes, cancellationToken);
            }

            // The put-away left the old cell (its open picks followed the boxes or were cut back): a virtual one that is now empty is free.
            if (hadStock)
            {
                await posting.ReleaseEmptyVirtualLocationsAsync([oldStockPosition], cancellationToken);
            }
        }

        var oldPosition = putaway.PalletPosition;
        if (excluded == PickerExcludedLocations.NoRack)
        {
            putaway.ExcludedLocation = excluded;
            putaway.OverrideReason = $"Set aside as {excluded} by picker";
            putaway.IsConfirmed = true;
            putaway.IsPickerPlaced = true;
            putaway.ConfirmedAt = clock.UtcNow;
            putaway.ConfirmedByUserId = currentUser.UserId;
            putaway.ConfirmedByUserName = currentUser.UserName;
        }
        else
        {
            // A pending order goes back to waiting for the supervisor's confirmation; a posted one stays posted.
            // This now applies to GIT too (it tracks a real, confirmable location, unlike NO RACK above) - a
            // pending order's GIT stock is only actually posted once the supervisor confirms the GRN
            // (see InwardPutawayConfirmationLogic.ConfirmAsync), so it must stay unconfirmed until then.
            putaway.ExcludedLocation = excluded;
            if (postedOrder)
            {
                putaway.IsConfirmed = true;
                putaway.ConfirmedAt = clock.UtcNow;
                putaway.ConfirmedByUserId = currentUser.UserId;
                putaway.ConfirmedByUserName = currentUser.UserName;
            }
            else
            {
                putaway.IsConfirmed = false;
                putaway.ConfirmedAt = null;
                putaway.ConfirmedByUserId = null;
                putaway.ConfirmedByUserName = null;
            }

            putaway.IsPickerPlaced = true;
            putaway.OverrideReason = excluded is null ? "Location set by picker" : $"Set aside as {excluded} by picker";
            if (newPosition!.Id != putaway.PalletPositionId)
            {
                putaway.PalletPositionId = newPosition.Id;
                putaway.PalletPosition = newPosition;
            }
        }

        oldPosition.RowVersion++;
        if (newPosition is not null)
        {
            newPosition.RowVersion++;
        }

        putaway.RowVersion++;
        await SaveAsync(cancellationToken);
        return new PickerPlaceInwardPutawayResult(true, null);
    }

    /// <summary>When the boxes a put-away needs to take out of its old cell are not all on the shelf (a pullout picked them after upload), the
    /// open picks of that cell cover the shortfall: each is re-pointed to the new cell (up to what the picker puts there) or cut back.
    /// Boxes a confirmed pick already shipped are simply gone from both sides. Returns the quantity to remove from the old cell, to add
    /// to the new one, and how much the picks hand back to the old cell and take from the new one.</summary>
    private async Task<(decimal Remove, decimal Add, decimal ReturnedToOld, decimal RepointedToNew)> FollowOpenPicksAsync(
        InwardTransactionLine line, PalletPosition oldPosition, int oldPkm, decimal originalQuantity,
        PalletPosition? target, int? newPkm, decimal newQuantity, string? excluded, CancellationToken cancellationToken)
    {
        var onHand = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == oldPosition.Id && b.MaterialId == line.MaterialId && b.MfgMonth == oldPkm)
            .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        var shortfall = Math.Max(0m, originalQuantity - onHand);
        if (shortfall <= 0)
        {
            return (originalQuantity, newQuantity, 0m, 0m);
        }

        var picks = await db.PulloutPicks
            .Include(p => p.PulloutTransactionLine).ThenInclude(l => l.PulloutTransaction)
            .Where(p => p.PalletPositionId == oldPosition.Id && p.MfgMonth == oldPkm
                && p.PulloutTransactionLine.MaterialId == line.MaterialId
                && p.PulloutTransactionLine.PulloutTransaction.IsConfirmed && !p.PulloutTransactionLine.PulloutTransaction.IsRejected
                && p.PulloutTransactionLine.PulloutTransaction.ConfirmedAt == null && p.PulloutTransactionLine.PulloutTransaction.UploadHash != null)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        var toCover = shortfall;
        var covered = 0m;
        var repointBudget = target is not null ? Math.Max(0m, newQuantity - Math.Max(0m, shortfall - picks.Sum(p => p.QuantityBoxes))) : 0m;
        var repointed = 0m;
        foreach (var pick in picks)
        {
            if (toCover <= 0)
            {
                break;
            }

            var take = Math.Min(pick.QuantityBoxes, toCover);
            toCover -= take;
            covered += take;
            var follow = Math.Min(take, repointBudget);
            repointBudget -= follow;
            repointed += follow;
            var cut = take - follow;
            var pickLine = pick.PulloutTransactionLine;
            // A pick that moves or shrinks has to be confirmed again by the picker (and its chosen remainder move no longer applies).
            pick.PickedAt = null;
            pick.PickedByUserId = null;
            pick.PickedByUserName = null;
            pick.PickedQuantityBoxes = null;
            pick.MoveToPalletPositionId = null;
            pick.MoveQuantityBoxes = null;

            if (follow > 0)
            {
                if (follow == pick.QuantityBoxes)
                {
                    pick.PalletPosition = target!;
                    pick.PalletPositionId = target!.Id;
                    pick.MfgMonth = newPkm!.Value;
                    pick.OverrideReason = "Follows the picker's put-away change";
                }
                else
                {
                    pick.QuantityBoxes -= follow;
                    pickLine.Picks.Add(new PulloutPick
                    {
                        PalletPosition = target!,
                        MfgMonth = newPkm!.Value,
                        QuantityBoxes = follow,
                        OverrideReason = "Follows the picker's put-away change"
                    });
                    cut = Math.Min(cut, pick.QuantityBoxes);
                }
            }

            if (cut > 0)
            {
                // These boxes are no longer available (set aside or fewer than expected): the pullout line is short by them.
                pick.QuantityBoxes -= cut;
                if (pick.QuantityBoxes <= 0)
                {
                    db.PulloutPicks.Remove(pick);
                }

                pickLine.PickedQuantityBoxes -= cut;
                pickLine.Status = pickLine.PickedQuantityBoxes >= pickLine.RequestedQuantityBoxes
                    ? AllocationStatus.Fulfilled
                    : pickLine.PickedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
                pickLine.Remarks = $"{cut:0.###} box(es) of the planned pick are no longer available" + (excluded is null ? " after the picker's put-away change." : $" - set aside as {excluded}.");
            }
        }

        // Whatever the picks could not cover was already shipped: it is gone from the old cell and never reaches the new one.
        var uncovered = toCover;
        return (originalQuantity - uncovered, Math.Max(0m, newQuantity - uncovered), covered, repointed);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("This put-away was just changed by another user. Reload the GRN and try again.");
        }
    }

    /// <summary>A line whose put-away total is below what the inward order asked for shows as Partial (or Failed when nothing is put
    /// away), so the shortfall stays visible instead of silently shrinking the order.</summary>
    private static void RefreshLineStatus(InwardTransactionLine line)
    {
        line.Status = line.AllocatedQuantityBoxes >= line.RequestedQuantityBoxes
            ? AllocationStatus.Fulfilled
            : line.AllocatedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
        line.Remarks = line.Status == AllocationStatus.Fulfilled
            ? null
            : $"Picker put away {line.AllocatedQuantityBoxes:0.###} of {line.RequestedQuantityBoxes:0.###} box(es).";
    }

    private static void MovePutawayToPkmLine(InwardTransaction transaction, InwardTransactionLine line, InwardPutaway putaway, decimal originalQuantity, int pkm)
    {
        var quantity = putaway.QuantityBoxes;
        var onlyPutawayOfLine = line.Putaways.Count == 1;
        var existing = transaction.Lines.FirstOrDefault(l => l != line && l.MaterialId == line.MaterialId && l.MfgMonth == pkm);

        if (existing is null && onlyPutawayOfLine)
        {
            line.MfgMonth = pkm;
            return;
        }

        var destination = existing ?? new InwardTransactionLine
        {
            InwardTransactionId = transaction.Id,
            MaterialId = line.MaterialId,
            Material = line.Material,
            MfgMonth = pkm,
            Status = AllocationStatus.Fulfilled
        };
        if (existing is null)
        {
            transaction.Lines.Add(destination);
        }

        line.Putaways.Remove(putaway);
        destination.Putaways.Add(putaway);
        // The ordered quantity of this location moves with it; only the boxes actually put away count as allocated.
        destination.RequestedQuantityBoxes += originalQuantity;
        destination.AllocatedQuantityBoxes += quantity;
        line.RequestedQuantityBoxes -= originalQuantity;
        line.AllocatedQuantityBoxes -= quantity;
    }

    /// <summary>A warning when the chosen location already holds confirmed stock or is reserved by another GRN. Warning only.</summary>
    private async Task<string?> BuildWarningAsync(InwardPutaway putaway, InwardTransactionLine line, PalletPosition target, CancellationToken cancellationToken)
    {
        if (target.Id == putaway.PalletPositionId)
        {
            return null;
        }

        var stock = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == target.Id && b.QuantityBoxes > 0)
            .Select(b => new { b.Material.MaterialNumber, b.QuantityBoxes })
            .ToListAsync(cancellationToken);
        var otherGrns = await db.InwardPutaways.AsNoTracking()
            .Where(p => p.PalletPositionId == target.Id && p.ExcludedLocation == null
                && p.InwardTransactionLine.InwardTransactionId != line.InwardTransactionId
                && !p.InwardTransactionLine.InwardTransaction.IsRejected)
            .Select(p => new { p.InwardTransactionLine.InwardTransaction.ReferenceNumber, p.IsConfirmed })
            .ToListAsync(cancellationToken);
        if (stock.Count == 0 && otherGrns.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (stock.Count > 0)
        {
            parts.Add($"holds {stock.Sum(s => s.QuantityBoxes):0.###} box(es) of stock ({string.Join(", ", stock.Select(s => s.MaterialNumber).Distinct())})");
        }

        var grns = otherGrns.Select(g => g.ReferenceNumber).Distinct().ToList();
        if (grns.Count > 0)
        {
            parts.Add($"is already used by {string.Join(", ", grns)}");
        }

        return $"Location {target.LocationCode} {string.Join(" and ", parts)}. Submit again to put it away here anyway.";
    }
}
