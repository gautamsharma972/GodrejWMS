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
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IPermissionService permissions, ICurrentUserService currentUser, IDateTimeProvider clock)
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
        if (putaway.ExcludedLocation is not null && putaway.IsExcludedLocked)
        {
            throw new InvalidOperationException("This GRN has already been confirmed, so GIT / NO RACK can no longer be corrected.");
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

        // PKM: the one entered here, else the line's own. Required only when the goods go into a real location.
        int? pkm = line.MfgMonth;
        if (!string.IsNullOrWhiteSpace(request.MfgMonthText))
        {
            MfgMonthParser.TryParse(request.MfgMonthText, out var parsed);
            pkm = parsed;
        }

        if (excluded is null && pkm is null)
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
        var hadStock = postedOrder && putaway.IsConfirmed && putaway.ExcludedLocation is null && line.MfgMonth is not null;
        var oldPkm = line.MfgMonth;
        var oldStockPosition = putaway.PalletPosition;
        // Remembered so the supervisor's Confirm can check the entries still add up to what the system allocated.
        putaway.SuggestedQuantityBoxes ??= originalQuantity;
        putaway.QuantityBoxes = request.QuantityBoxes;
        line.AllocatedQuantityBoxes += request.QuantityBoxes - originalQuantity;

        // PKM: the put-away belongs to the line carrying its PKM (stock is keyed by line PKM), so it moves to the
        // GRN's line for this material + PKM (created when none exists). A lone no-PKM line just takes the PKM.
        if (excluded is null && pkm != line.MfgMonth)
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
            if (hadStock)
            {
                changes.Add(new StockChange(line.MaterialId, oldPkm!.Value, LocationSubtypeIds.Good, oldStockPosition, -originalQuantity));
            }

            if (excluded is null)
            {
                changes.Add(new StockChange(line.MaterialId, pkm!.Value, LocationSubtypeIds.Good, target!, request.QuantityBoxes));
            }

            if (changes.Count > 0)
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.PutawayChange, "GRN", transaction.ReferenceNumber,
                        excluded is not null ? $"Set aside as {excluded} by picker" : "Location set by picker"),
                    changes, cancellationToken);
            }
        }

        var oldPosition = putaway.PalletPosition;
        if (excluded is not null)
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
            putaway.ExcludedLocation = null;
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
            putaway.OverrideReason = "Location set by picker";
            if (target!.Id != putaway.PalletPositionId)
            {
                putaway.PalletPositionId = target.Id;
                putaway.PalletPosition = target;
            }
        }

        oldPosition.RowVersion++;
        if (target is not null)
        {
            target.RowVersion++;
        }

        putaway.RowVersion++;
        await SaveAsync(cancellationToken);
        return new PickerPlaceInwardPutawayResult(true, null);
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
