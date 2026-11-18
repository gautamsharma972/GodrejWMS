using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Refresh Put-away for a GRN already posted to inventory (post-on-upload). Every put-away the picker has already acted on
/// (placed, changed, set aside as GIT / NO RACK, or locked) is left exactly as it is. The untouched ones are taken back
/// out of their locations and the same boxes are allocated again with the current rules, for instance after a material's
/// pallet size changed, and posted to the new locations. Boxes picked or moved since the upload are not re-created.
/// </summary>
[RequiresPermission(PermissionKeys.Inward.Edit, PermissionKeys.Inward.Putaway, PermissionKeys.Inward.Reallocate)]
public sealed record RefreshPostedInwardPutawayCommand(int TransactionId) : IRequest<InwardResultDto>;

public sealed class RefreshPostedInwardPutawayValidator : AbstractValidator<RefreshPostedInwardPutawayCommand>
{
    public RefreshPostedInwardPutawayValidator() => RuleFor(x => x.TransactionId).GreaterThan(0);
}

public sealed class RefreshPostedInwardPutawayHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator,
    IStockPostingService posting,
    IVirtualLocationService virtualLocations,
    IUnitOfWork uow,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<RefreshPostedInwardPutawayCommand, InwardResultDto>
{
    public Task<InwardResultDto> Handle(RefreshPostedInwardPutawayCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    /// <summary>True for a put-away nobody has acted on yet.</summary>
    public static bool IsUntouched(InwardPutaway p) =>
        p.IsConfirmed && !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation is null;

    /// <summary>A reservation on a posted GRN that never became stock (e.g. made by the old Reallocate): it holds boxes but no inventory.</summary>
    public static bool IsPendingReservation(InwardPutaway p) => !p.IsConfirmed && !p.IsPickerPlaced && p.ExcludedLocation is null;

    private async Task<InwardResultDto> RunAsync(RefreshPostedInwardPutawayCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Putaways).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected and its put-away cannot be refreshed.");
        }
        if (transaction.PostedAt is null)
        {
            throw new InvalidOperationException("This GRN has not been posted to inventory; use the normal put-away refresh.");
        }

        // An inactive material must never be given a location; those lines are handled by the release of inactive materials.
        var materialIds = transaction.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var inactive = (await db.Materials.AsNoTracking()
            .Where(m => materialIds.Contains(m.Id) && !m.IsActive).Select(m => m.Id).ToListAsync(cancellationToken)).ToHashSet();

        // Boxes that were put away and have since left inventory (picked by a pullout, moved, adjusted away) are NOT given a new location:
        // only boxes this GRN never posted count as a gap (see GrnPostedQuantity).
        var netPosted = await GrnPostedQuantity.NetPostedAsync(db, transaction.ReferenceNumber, cancellationToken);
        var recorded = await InwardVariances.AccountedAsync(db, transaction.Id, cancellationToken);
        // A put-away a supervisor recorded damage / shortage / mismatch against keeps its cell: its Damage stock sits there, so the row is never re-laid out.
        var rowsWithRecords = (await db.InwardLineVariances.AsNoTracking()
                .Where(v => v.InwardTransactionId == transaction.Id && v.InwardPutawayId != null)
                .Select(v => v.InwardPutawayId!.Value).ToListAsync(cancellationToken))
            .ToHashSet();
        bool Untouched(InwardPutaway p) => IsUntouched(p) && !rowsWithRecords.Contains(p.Id);
        var lines = transaction.Lines
            .Where(l => l.MfgMonth is not null && !inactive.Contains(l.MaterialId)
                && (l.Putaways.Any(Untouched) || l.Putaways.Any(IsPendingReservation)
                    || GrnPostedQuantity.UnplacedBoxes(transaction.Lines, l, netPosted, recorded) > 0))
            .OrderBy(l => l.Id)
            .ToList();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Nothing to refresh: the picker has actioned every put-away and no boxes are waiting for a location. "
                + "Boxes that were put away and have since been picked, moved or adjusted away are not given a new location.");
        }

        // Boxes a line has no put-away row for and that were never posted (e.g. its material was inactive or the warehouse was full at
        // upload) are allocated now too. Counted from the rows themselves, not from the line's Allocated figure, which can leave out reservations.
        var gapBefore = new Dictionary<int, decimal>();
        foreach (var group in lines.GroupBy(l => (l.MaterialId, l.MfgMonth)))
        {
            var first = true;
            foreach (var l in group)
            {
                gapBefore[l.Id] = first ? GrnPostedQuantity.UnplacedBoxes(transaction.Lines, l, netPosted, recorded) : 0m;
                first = false;
            }
        }
        // Reservations that never became stock are dropped here; their boxes are allocated and posted below like the rest.
        var pendingBoxes = new Dictionary<int, decimal>();
        foreach (var line in lines)
        {
            var pending = line.Putaways.Where(IsPendingReservation).ToList();
            pendingBoxes[line.Id] = pending.Sum(p => p.QuantityBoxes);
            foreach (var putaway in pending)
            {
                putaway.PalletPosition.RowVersion++;
                line.Putaways.Remove(putaway);
                db.InwardPutaways.Remove(putaway);
            }
        }

        var untouched = lines.SelectMany(l => l.Putaways.Where(Untouched).Select(p => (Line: l, Putaway: p))).ToList();
        var untouchedByMaterial = untouched
            .GroupBy(x => (x.Line.MaterialId, x.Line.MfgMonth!.Value))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Putaway.QuantityBoxes));

        // 1. Take the untouched put-aways back out of their locations (only what is still in place).
        var plan = await PostedStockReversal.PlanAsync(
            db,
            untouched.Select(x => new StockReversalItem(x.Line.MaterialId, x.Line.MfgMonth!.Value, x.Putaway.PalletPosition, x.Putaway.QuantityBoxes)).ToList(),
            cancellationToken);
        if (plan.Changes.Count > 0)
        {
            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.Reversal, "GRN", transaction.ReferenceNumber, "Put-away refreshed"),
                plan.Changes, cancellationToken);
        }

        foreach (var (line, putaway) in untouched)
        {
            putaway.PalletPosition.RowVersion++;
            line.Putaways.Remove(putaway);
            db.InwardPutaways.Remove(putaway);
        }
        await db.SaveChangesAsync(cancellationToken); // the allocator reads the freed space from the database

        // 2. Allocate what was taken back with the current rules and post it to the new locations.
        var now = clock.UtcNow;
        foreach (var line in lines)
        {
            var changes = new List<StockChange>();
            // A material can sit on several lines of one GRN; the boxes taken back for it are handed out on its first line.
            var sameMaterial = lines.Where(l => l.MaterialId == line.MaterialId && l.MfgMonth == line.MfgMonth).ToList();
            var kept = line.Putaways.Where(p => p.IsConfirmed).Sum(p => p.QuantityBoxes);
            if (sameMaterial[0] != line)
            {
                line.AllocatedQuantityBoxes = kept;
                continue;
            }

            var wanted = plan.ReversedByMaterial.GetValueOrDefault((line.MaterialId, line.MfgMonth!.Value))
                + sameMaterial.Sum(l => gapBefore[l.Id] + pendingBoxes[l.Id]);
            // Never more than the GRN asked for, whatever an earlier action left behind.
            var room = Math.Max(0, sameMaterial.Sum(l => l.RequestedQuantityBoxes) - sameMaterial.Sum(l => l.Putaways.Sum(p => p.QuantityBoxes)));
            var quantity = Math.Min(wanted, room);
            // Put-aways whose stock was no longer in its cell (picked, moved or adjusted away) are dropped, not re-allocated:
            // say so on the line instead of leaving an unexplained shortfall.
            var gone = untouchedByMaterial.GetValueOrDefault((line.MaterialId, line.MfgMonth!.Value))
                - plan.ReversedByMaterial.GetValueOrDefault((line.MaterialId, line.MfgMonth!.Value));
            var goneNote = gone > 0
                ? $" {gone:0.###} box(es) were no longer in inventory at their put-away location (picked, moved or adjusted away) and were not re-allocated."
                : string.Empty;
            if (quantity <= 0)
            {
                line.AllocatedQuantityBoxes = kept;
                if (gone > 0)
                {
                    line.Status = kept >= line.RequestedQuantityBoxes ? AllocationStatus.Fulfilled : kept > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
                    line.Remarks = goneNote.Trim();
                }

                continue;
            }

            var allocation = await allocator.AllocateAsync(
                line.MaterialId, quantity, line.MfgMonth, transaction.WarehouseId,
                allowVirtualOverflow: true, cancellationToken: cancellationToken);

            foreach (var suggestion in allocation.Lines)
            {
                var position = await db.PalletPositions.FirstAsync(p => p.LocationCode == suggestion.LocationCode, cancellationToken);
                position.RowVersion++;
                line.Putaways.Add(NewPutaway(position, suggestion.QuantityBoxes, suggestion.Reason, now));
                changes.Add(new StockChange(line.MaterialId, line.MfgMonth!.Value, LocationSubtypeIds.Good, position, suggestion.QuantityBoxes));
            }

            foreach (var chunk in allocation.OverflowChunks ?? [])
            {
                var position = await virtualLocations.AcquireAsync(transaction.WarehouseId, transaction.Id, cancellationToken);
                line.Putaways.Add(NewPutaway(position, chunk, InwardPlacementService.OverflowReason, now));
                changes.Add(new StockChange(line.MaterialId, line.MfgMonth!.Value, LocationSubtypeIds.Good, position, chunk));
            }

            line.AllocatedQuantityBoxes = kept + allocation.AllocatedQuantityBoxes;
            line.Status = line.AllocatedQuantityBoxes >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : line.AllocatedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
            line.Remarks = line.Status == AllocationStatus.Fulfilled && gone <= 0
                ? null
                : ((line.Status == AllocationStatus.Fulfilled ? null : allocation.Remarks) + goneNote).Trim();

            // Posted line by line: the allocator sees only posted stock and unconfirmed reservations, so the next material would
            // otherwise be given the very locations this one has just taken (two materials in one location).
            if (changes.Count > 0)
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.InwardPost, "GRN", transaction.ReferenceNumber, "Put-away refreshed"),
                    changes, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken); // a second pallet must not be handed the location the first now occupies
        }

        await db.SaveChangesAsync(cancellationToken);
        return InwardResultMapper.ToResult(transaction);
    }

    private InwardPutaway NewPutaway(PalletPosition position, decimal quantity, string? reason, DateTimeOffset now) => new()
    {
        PalletPosition = position,
        PalletPositionId = position.Id,
        QuantityBoxes = quantity,
        AllocationReason = reason,
        IsConfirmed = true,
        ConfirmedAt = now,
        ConfirmedByUserId = currentUser.UserId,
        ConfirmedByUserName = currentUser.UserName
    };
}
