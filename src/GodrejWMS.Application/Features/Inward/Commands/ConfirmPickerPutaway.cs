using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>The picker's final Confirm of a GRN, once every location has been submitted. Until then the picker can keep changing
/// locations, PKM and GIT / NO RACK entries; confirming locks every row of the GRN against further picker changes and takes it off the
/// picker's pending list. For a GRN posted on upload the stock is already in inventory; for an older GRN a supervisor still posts it.</summary>
public sealed record ConfirmPickerPutawayCommand(int TransactionId) : IRequest<ConfirmPickerPutawayResult>;

public sealed record ConfirmPickerPutawayResult(string ReferenceNumber, int LocationCount, bool StockPosted);

public sealed class ConfirmPickerPutawayValidator : AbstractValidator<ConfirmPickerPutawayCommand>
{
    public ConfirmPickerPutawayValidator() => RuleFor(x => x.TransactionId).GreaterThan(0);
}

public sealed class ConfirmPickerPutawayHandler(
    IApplicationDbContext db, IPermissionService permissions, IStockPostingService posting, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<ConfirmPickerPutawayCommand, ConfirmPickerPutawayResult>
{
    public async Task<ConfirmPickerPutawayResult> Handle(ConfirmPickerPutawayCommand request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Inward.AssignPkm, cancellationToken))
        {
            throw new InvalidOperationException("You do not have permission to confirm put-away.");
        }

        var transaction = await db.InwardTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Putaways).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected.");
        }

        var putaways = transaction.Lines.SelectMany(l => l.Putaways).ToList();
        // The long-lived Blazor context may hold stale copies of rows another session just changed.
        foreach (var putaway in putaways)
        {
            await db.Entry(putaway).ReloadAsync(cancellationToken);
        }

        // A row a supervisor already confirmed on an older GRN counts as done; everything else needs the picker's own submission.
        var notSubmitted = putaways.Count(p => !p.IsPickerPlaced && !(transaction.PostedAt is null && p.IsConfirmed));
        if (notSubmitted > 0)
        {
            throw new InvalidOperationException(
                $"Submit every location first - {notSubmitted} location(s) of this GRN have not been confirmed or changed yet.");
        }

        // An order that was not posted on upload (Inward without PKM, or one from before posting on upload) reaches inventory now, on the
        // picker's Confirm - no separate confirmation on the Goods Receipt Audit screen is needed.
        if (transaction.PostedAt is null)
        {
            await PostToInventoryAsync(transaction, cancellationToken);
        }

        foreach (var putaway in putaways)
        {
            putaway.IsExcludedLocked = true;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("This put-away was just changed by another user. Please refresh and try again.");
        }
        return new ConfirmPickerPutawayResult(transaction.ReferenceNumber, putaways.Count, transaction.PostedAt is not null);
    }

    private async Task PostToInventoryAsync(InwardTransaction transaction, CancellationToken cancellationToken)
    {
        var withoutPkm = transaction.Lines
            .Where(l => l.MfgMonth is null)
            .SelectMany(l => l.Putaways.Where(p => !p.IsConfirmed))
            .Select(p => p.PalletPosition.LocationCode)
            .ToList();
        if (withoutPkm.Count > 0)
        {
            throw new InvalidOperationException($"Assign a PKM for {string.Join(", ", withoutPkm)} before confirming.");
        }

        // The picker's entries for each material must add up to exactly what the system allocated.
        foreach (var material in transaction.Lines.GroupBy(l => l.Material))
        {
            var rows = material.SelectMany(l => l.Putaways).ToList();
            var allocated = rows.Sum(p => p.SuggestedQuantityBoxes ?? p.QuantityBoxes);
            var entered = rows.Sum(p => p.QuantityBoxes);
            if (entered != allocated)
            {
                throw new InvalidOperationException(
                    $"Qty cannot be {(entered < allocated ? "less" : "more")} than the {allocated:0.###} box(es) the system allocated for material {material.Key.MaterialNumber} on this GRN. Total entered is {entered:0.###}.");
            }
        }

        foreach (var line in transaction.Lines)
        {
            foreach (var putaway in line.Putaways.Where(p => !p.IsConfirmed))
            {
                await InwardPutawayConfirmationLogic.ConfirmAsync(db, posting, line, putaway, clock, currentUser, cancellationToken);
            }
        }
    }
}
