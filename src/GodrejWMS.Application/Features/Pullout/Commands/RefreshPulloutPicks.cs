using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Refresh Pick: for a pullout whose pick-list is not confirmed yet. Picks that were taken from PICK-RESERVED boxes of an inward
/// GRN that has since been rejected point at stock that never existed, so they are dropped (nothing to return - the GRN's rejection
/// skipped them) and the missing quantity - plus any earlier shortfall - is allocated again from the stock available now. Picks that are
/// still valid, including ones the picker already submitted, are left alone.</summary>
[RequiresPermission(PermissionKeys.Pullout.Edit)]
public sealed record RefreshPulloutPicksCommand(string ReferenceNumber) : IRequest<RefreshPulloutPicksResult>;

public sealed record RefreshPulloutPicksResult(string ReferenceNumber, decimal DroppedBoxes, decimal AllocatedBoxes, decimal StillShortBoxes);

public sealed class RefreshPulloutPicksValidator : AbstractValidator<RefreshPulloutPicksCommand>
{
    public RefreshPulloutPicksValidator() => RuleFor(x => x.ReferenceNumber).NotEmpty();
}

public sealed class RefreshPulloutPicksHandler(
    IApplicationDbContext db,
    IPulloutAllocationService allocator,
    IStockPostingService posting,
    IUnitOfWork uow,
    IDateTimeProvider clock) : IRequestHandler<RefreshPulloutPicksCommand, RefreshPulloutPicksResult>
{
    public Task<RefreshPulloutPicksResult> Handle(RefreshPulloutPicksCommand request, CancellationToken cancellationToken) =>
        InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);

    /// <summary>Boxes of this pullout's picks that sit on PICK-RESERVED put-aways of a rejected GRN, per (material, PKM, position).</summary>
    public static async Task<Dictionary<(int MaterialId, int MfgMonth, int PositionId), decimal>> FindInvalidAsync(
        IApplicationDbContext db, string pulloutReference, CancellationToken cancellationToken)
    {
        var reason = PutawayPickReservation.ReasonFor(pulloutReference);
        var rows = await db.InwardPutaways.AsNoTracking()
            .Where(p => p.ExcludedLocation == PutawayPickReservation.Label && p.OverrideReason == reason
                && p.InwardTransactionLine.InwardTransaction.IsRejected)
            .Select(p => new { p.InwardTransactionLine.MaterialId, p.InwardTransactionLine.MfgMonth, p.PalletPositionId, p.QuantityBoxes })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<(int, int, int), decimal>();
        foreach (var row in rows.Where(r => r.MfgMonth is not null))
        {
            var key = (row.MaterialId, row.MfgMonth!.Value, row.PalletPositionId);
            result[key] = result.GetValueOrDefault(key) + row.QuantityBoxes;
        }

        return result;
    }

    private async Task<RefreshPulloutPicksResult> RunAsync(RefreshPulloutPicksCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.PulloutTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.ReferenceNumber == request.ReferenceNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.ReferenceNumber);
        await db.Entry(transaction).ReloadAsync(cancellationToken);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This pullout was rejected and cannot be refreshed.");
        }

        if (!transaction.IsConfirmed || transaction.UploadHash is null || transaction.ConfirmedAt is not null)
        {
            throw new InvalidOperationException("Refresh Pick is only available while the pick-list is not confirmed yet.");
        }

        var invalid = await FindInvalidAsync(db, transaction.ReferenceNumber, cancellationToken);
        var dropped = 0m;
        foreach (var line in transaction.Lines)
        {
            foreach (var pick in line.Picks.OrderBy(p => p.Id).ToList())
            {
                var key = (line.MaterialId, pick.MfgMonth, pick.PalletPositionId);
                if (!invalid.TryGetValue(key, out var left) || left <= 0)
                {
                    continue;
                }

                var cut = Math.Min(pick.QuantityBoxes, left);
                invalid[key] = left - cut;
                dropped += cut;
                line.PickedQuantityBoxes -= cut;
                pick.QuantityBoxes -= cut;
                if (pick.QuantityBoxes <= 0)
                {
                    line.Picks.Remove(pick);
                    db.PulloutPicks.Remove(pick);
                }
                else
                {
                    // A shrunk pick has to be confirmed again by the picker.
                    pick.PickedAt = null;
                    pick.PickedByUserId = null;
                    pick.PickedByUserName = null;
                    pick.PickedQuantityBoxes = null;
                    pick.MoveToPalletPositionId = null;
                    pick.MoveQuantityBoxes = null;
                }
            }
        }

        // The rows of the rejected GRN stay as history but are no longer this pullout's live reservations.
        var reason = PutawayPickReservation.ReasonFor(transaction.ReferenceNumber);
        var released = await db.InwardPutaways
            .Where(p => p.ExcludedLocation == PutawayPickReservation.Label && p.OverrideReason == reason
                && p.InwardTransactionLine.InwardTransaction.IsRejected)
            .ToListAsync(cancellationToken);
        foreach (var row in released)
        {
            row.OverrideReason = $"{reason} - released, GRN rejected";
            row.RowVersion++;
        }

        // Allocate what is missing, one line at a time, deducting before the next line so the allocator never offers the same boxes twice.
        var allocated = 0m;
        var stillShort = 0m;
        foreach (var line in transaction.Lines.OrderBy(l => l.Id))
        {
            var missing = line.RequestedQuantityBoxes - line.PickedQuantityBoxes;
            if (missing > 0)
            {
                var allocation = await allocator.AllocateAsync(
                    line.MaterialId, missing, transaction.WarehouseId, cancellationToken, preview: true);
                var newPicks = new List<PulloutPick>();
                foreach (var pick in allocation.Picks)
                {
                    var position = await db.PalletPositions.Include(p => p.Rack)
                        .FirstAsync(p => p.LocationCode == pick.LocationCode, cancellationToken);
                    newPicks.Add(new PulloutPick
                    {
                        PalletPosition = position, PalletPositionId = position.Id, MfgMonth = pick.MfgMonth,
                        QuantityBoxes = pick.QuantityBoxes, OverrideReason = "Pick refreshed"
                    });
                }

                if (newPicks.Count > 0)
                {
                    var probe = new PulloutTransactionLine { MaterialId = line.MaterialId };
                    foreach (var p in newPicks)
                    {
                        probe.Picks.Add(new PulloutPick { PalletPositionId = p.PalletPositionId, MfgMonth = p.MfgMonth, QuantityBoxes = p.QuantityBoxes });
                    }

                    await PutawayPickReservation.ReserveAsync(db, transaction.ReferenceNumber, [probe], clock.UtcNow, cancellationToken);
                    await posting.ApplyAsync(
                        new StockPostingContext(StockLedgerEntryTypes.PulloutDeduct, "PICK", transaction.ReferenceNumber, "Pick refreshed"),
                        newPicks.Select(p => new StockChange(line.MaterialId, p.MfgMonth, LocationSubtypeIds.Good, p.PalletPosition, -p.QuantityBoxes)).ToList(),
                        cancellationToken);
                    foreach (var p in newPicks)
                    {
                        line.Picks.Add(p);
                    }

                    line.PickedQuantityBoxes += allocation.PickedQuantityBoxes;
                    allocated += allocation.PickedQuantityBoxes;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            line.Status = line.PickedQuantityBoxes >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : line.PickedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
            var short_ = Math.Max(0m, line.RequestedQuantityBoxes - line.PickedQuantityBoxes);
            stillShort += short_;
            line.Remarks = short_ <= 0
                ? null
                : $"Insufficient good stock - {short_:0.###} box(es) short across all pickable locations.";
        }

        // Keep the saved preview in step with the picks.
        if (transaction.PreviewJson is not null)
        {
            var preview = JsonSerializer.Deserialize<PulloutResultDto>(transaction.PreviewJson)!;
            var lines = transaction.Lines.OrderBy(l => l.Id).Select(l => new PulloutLineResultDto(
                l.Material.MaterialNumber, l.Material.Description, l.RequestedQuantityBoxes, l.PickedQuantityBoxes, l.Status, l.Remarks,
                l.Picks.OrderBy(p => p.MfgMonth).Select(p => new PulloutPickDto(p.PalletPosition.LocationCode, MfgMonthParser.Format(p.MfgMonth), p.QuantityBoxes)).ToList(),
                l.PlantName, l.Priority)).ToList();
            transaction.PreviewJson = JsonSerializer.Serialize(preview with { Lines = lines });
        }

        transaction.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return new RefreshPulloutPicksResult(transaction.ReferenceNumber, dropped, allocated, stillShort);
    }
}
