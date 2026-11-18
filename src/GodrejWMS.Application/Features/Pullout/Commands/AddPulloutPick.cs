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

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>A stock location of a material the supervisor can add to an open pick-list, oldest PKM first.</summary>
public sealed record PulloutAddOptionDto(
    int PalletPositionId, string LocationCode, string MfgMonthLabel, decimal QuantityBoxes, bool IsVirtual, bool BreaksFifo);

/// <summary>While the vehicle still has room, a supervisor adds material to an open pick-list by choosing the material code and the location to
/// pick it from. The boxes are deducted from that location straight away, like every pick of a posted pullout, and become a new pick (or more
/// boxes on the material's existing line) that still has to be submitted and confirmed. Picking from a newer PKM than the oldest one in stock
/// breaks FIFO and needs a reason.</summary>
[RequiresPermission(PermissionKeys.Pullout.Edit)]
public sealed record AddPulloutPickCommand(int PulloutId, int MaterialId, int PalletPositionId, decimal QuantityBoxes, string? OverrideReason = null)
    : IRequest<AddPulloutPickResult>;

public sealed record AddPulloutPickResult(string ReferenceNumber, string LocationCode, decimal AddedBoxes, bool BreaksFifo);

public sealed class AddPulloutPickValidator : AbstractValidator<AddPulloutPickCommand>
{
    public AddPulloutPickValidator()
    {
        RuleFor(x => x.PulloutId).GreaterThan(0);
        RuleFor(x => x.MaterialId).GreaterThan(0).WithMessage("Select the material.");
        RuleFor(x => x.PalletPositionId).GreaterThan(0).WithMessage("Select the location.");
        RuleFor(x => x.QuantityBoxes).GreaterThan(0).Must(decimal.IsInteger).WithMessage("Quantity must be a whole number greater than zero.");
        RuleFor(x => x.OverrideReason).MaximumLength(250);
    }
}

public sealed class AddPulloutPickHandler(
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock)
    : IRequestHandler<AddPulloutPickCommand, AddPulloutPickResult>
{
    public Task<AddPulloutPickResult> Handle(AddPulloutPickCommand request, CancellationToken cancellationToken) =>
        InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);

    /// <summary>Good stock of the material in the pullout's warehouse, by location, oldest PKM first. Used by the screen and by the command.</summary>
    public static async Task<IReadOnlyList<PulloutAddOptionDto>> OptionsAsync(
        IApplicationDbContext db, int warehouseId, int materialId, CancellationToken cancellationToken)
    {
        var batches = await db.StockBatches.AsNoTracking()
            .Where(b => b.MaterialId == materialId && b.StockSubtypeId == LocationSubtypeIds.Good && b.QuantityBoxes > 0
                && b.PalletPosition.IsActive && b.PalletPosition.LocationSubtypeId == LocationSubtypeIds.Good
                && b.PalletPosition.Rack.WarehouseId == warehouseId && b.PalletPosition.ZoneTypeId != ZoneTypeIds.Git)
            .Select(b => new { b.PalletPositionId, b.PalletPosition.LocationCode, b.PalletPosition.IsVirtual, b.MfgMonth, b.QuantityBoxes })
            .ToListAsync(cancellationToken);
        var oldest = batches.Count == 0 ? 0 : batches.Min(b => b.MfgMonth);
        return batches
            .OrderBy(b => b.MfgMonth).ThenBy(b => b.LocationCode)
            .Select(b => new PulloutAddOptionDto(b.PalletPositionId, b.LocationCode, MfgMonthParser.Format(b.MfgMonth), b.QuantityBoxes, b.IsVirtual, b.MfgMonth > oldest))
            .ToList();
    }

    private async Task<AddPulloutPickResult> RunAsync(AddPulloutPickCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.PulloutTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.PulloutId, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.PulloutId);
        await db.Entry(transaction).ReloadAsync(cancellationToken);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This pullout was rejected.");
        }

        if (!transaction.IsConfirmed || transaction.UploadHash is null || transaction.ConfirmedAt is not null)
        {
            throw new InvalidOperationException("Material can only be added while the pick-list is open (not confirmed yet).");
        }

        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException(nameof(Material), request.MaterialId);
        if (!material.IsActive)
        {
            throw new InvalidOperationException($"Material {material.MaterialNumber} is inactive in Material Master.");
        }

        var position = await db.PalletPositions.Include(p => p.Rack)
            .FirstOrDefaultAsync(p => p.Id == request.PalletPositionId, cancellationToken)
            ?? throw new InvalidOperationException("The selected location was not found.");
        if (!position.IsActive || position.Rack.WarehouseId != transaction.WarehouseId
            || position.LocationSubtypeId != LocationSubtypeIds.Good || position.ZoneTypeId == ZoneTypeIds.Git)
        {
            throw new InvalidOperationException($"Location {position.LocationCode} cannot be picked from for this pullout.");
        }

        var options = (await OptionsAsync(db, transaction.WarehouseId, material.Id, cancellationToken))
            .Where(o => o.PalletPositionId == position.Id).ToList();
        var available = options.Sum(o => o.QuantityBoxes);
        if (available < request.QuantityBoxes)
        {
            throw new InvalidOperationException(
                $"{position.LocationCode} holds {available:0.###} box(es) of material {material.MaterialNumber}; {request.QuantityBoxes:0.###} cannot be added.");
        }

        // Oldest PKM at this location first. A newer PKM than the oldest one in stock anywhere breaks FIFO and needs a reason.
        var remaining = request.QuantityBoxes;
        var taken = new List<(int MfgMonth, decimal Boxes, bool BreaksFifo)>();
        foreach (var option in options)
        {
            MfgMonthParser.TryParse(option.MfgMonthLabel, out var mfgMonth);
            var take = Math.Min(remaining, option.QuantityBoxes);
            taken.Add((mfgMonth, take, option.BreaksFifo));
            remaining -= take;
            if (remaining <= 0)
            {
                break;
            }
        }

        var breaksFifo = taken.Any(t => t.BreaksFifo);
        if (breaksFifo && string.IsNullOrWhiteSpace(request.OverrideReason))
        {
            throw new InvalidOperationException(
                $"Older stock of material {material.MaterialNumber} exists elsewhere, so picking from {position.LocationCode} breaks FIFO. Enter a reason to continue.");
        }

        var reason = string.IsNullOrWhiteSpace(request.OverrideReason) ? "Added by supervisor" : $"Added by supervisor: {request.OverrideReason.Trim()}";
        var line = transaction.Lines.FirstOrDefault(l => l.MaterialId == material.Id);
        if (line is null)
        {
            line = new PulloutTransactionLine { MaterialId = material.Id, Material = material, RequestedQuantityBoxes = 0, PickedQuantityBoxes = 0 };
            transaction.Lines.Add(line);
        }

        // Boxes of an inward put-away the picker has not placed yet become PICK-RESERVED, before the deduction (see PutawayPickReservation).
        var probe = new PulloutTransactionLine { MaterialId = material.Id };
        foreach (var (mfgMonth, boxes, _) in taken)
        {
            probe.Picks.Add(new PulloutPick { PalletPositionId = position.Id, MfgMonth = mfgMonth, QuantityBoxes = boxes });
        }

        await PutawayPickReservation.ReserveAsync(db, transaction.ReferenceNumber, [probe], clock.UtcNow, cancellationToken);
        await posting.ApplyAsync(
            new StockPostingContext(StockLedgerEntryTypes.PulloutDeduct, "PICK", transaction.ReferenceNumber, "Material added to the pick-list by a supervisor"),
            taken.Select(t => new StockChange(material.Id, t.MfgMonth, LocationSubtypeIds.Good, position, -t.Boxes)).ToList(), cancellationToken);

        foreach (var (mfgMonth, boxes, _) in taken)
        {
            line.Picks.Add(new PulloutPick { PalletPosition = position, PalletPositionId = position.Id, MfgMonth = mfgMonth, QuantityBoxes = boxes, OverrideReason = reason });
        }

        line.RequestedQuantityBoxes += request.QuantityBoxes;
        line.PickedQuantityBoxes += request.QuantityBoxes;
        line.Status = line.PickedQuantityBoxes >= line.RequestedQuantityBoxes ? AllocationStatus.Fulfilled : AllocationStatus.Partial;
        line.Remarks = line.Status == AllocationStatus.Fulfilled ? null : line.Remarks;

        // The picker's (or supervisor's) pick-list confirmation now has an open pick again.
        RefreshPulloutPicksHandler.RebuildPreview(transaction);
        transaction.RowVersion++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException("Stock or this pick-list was just changed by another user. Reload and try again.");
        }

        return new AddPulloutPickResult(transaction.ReferenceNumber, position.LocationCode, request.QuantityBoxes, breaksFifo);
    }
}
