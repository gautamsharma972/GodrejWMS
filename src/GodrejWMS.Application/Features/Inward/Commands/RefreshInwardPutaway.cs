using GodrejWMS.Application.Common.Permissions;
using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Replaces every unconfirmed reservation on a pending GRN using the current put-away rules.
/// Confirmed stock is immutable. Lines are refreshed sequentially so each saved line reserves
/// capacity before the next line is allocated.
/// </summary>
[RequiresPermission(PermissionKeys.Inward.Edit, PermissionKeys.Inward.Putaway, PermissionKeys.Inward.Reallocate)]
public sealed record RefreshInwardPutawayCommand(int TransactionId) : IRequest<InwardResultDto>;

public sealed class RefreshInwardPutawayValidator : AbstractValidator<RefreshInwardPutawayCommand>
{
    public RefreshInwardPutawayValidator() => RuleFor(x => x.TransactionId).GreaterThan(0);
}

public sealed class RefreshInwardPutawayHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator) : IRequestHandler<RefreshInwardPutawayCommand, InwardResultDto>
{
    private const int MaxAllocationAttempts = 3;

    public async Task<InwardResultDto> Handle(RefreshInwardPutawayCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected and its put-away cannot be refreshed.");
        }
        if (transaction.Lines.SelectMany(x => x.Putaways).Any()
            && transaction.Lines.SelectMany(x => x.Putaways).All(x => x.IsConfirmed)
            && transaction.Lines.All(x => x.AllocatedQuantityBoxes >= x.RequestedQuantityBoxes))
        {
            throw new InvalidOperationException("This GRN has already been fully confirmed.");
        }

        // GRNs created before upload rows were merged can carry the same material + PKM on several lines, which splits
        // one pallet across rows (e.g. 7 + 3 on one location). Fold them into one line first, so the allocation below
        // lays the combined quantity out the way a fresh upload would.
        MergeDuplicateLines(transaction);

        var orderedLines = transaction.Lines.OrderBy(x => x.Id).ToList();

        // Release every pending reservation of this GRN before allocating anything. Refreshing line by line while the
        // later lines still held their old reservations made each earlier line top up around space that was about to be
        // freed, so one pallet ended up split across lines in odd amounts (e.g. 42 on a 54-box pallet already carrying
        // 12 from the next line). With the GRN's own pending space released first, the lines are re-allocated in order
        // exactly as they were when the GRN was first submitted. A failure from here on leaves lines under-reserved
        // (and marked so), never double-booked, and a second refresh recovers it.
        await ReleasePendingAsync(orderedLines, cancellationToken);

        foreach (var line in orderedLines)
        {
            await RefreshLineAsync(line, transaction.WarehouseId, cancellationToken);
        }

        return InwardResultMapper.ToResult(transaction);
    }

    /// <summary>Folds lines of the same material and Mfg Month into the oldest one: requested quantities are added
    /// together and already-confirmed put-aways move to that line (confirmed stock is never touched or re-laid-out).
    /// The duplicates' unconfirmed reservations are dropped here, since the refresh re-allocates them anyway.</summary>
    private void MergeDuplicateLines(InwardTransaction transaction)
    {
        foreach (var group in transaction.Lines.GroupBy(l => (l.MaterialId, l.MfgMonth)).Where(g => g.Count() > 1).ToList())
        {
            var survivor = group.OrderBy(l => l.Id).First();
            survivor.RequestedQuantityBoxes = group.Sum(l => l.RequestedQuantityBoxes);

            foreach (var duplicate in group.Where(l => l != survivor).ToList())
            {
                foreach (var putaway in duplicate.Putaways.ToList())
                {
                    if (putaway.IsConfirmed)
                    {
                        putaway.InwardTransactionLine = survivor;
                        putaway.InwardTransactionLineId = survivor.Id;
                    }
                    else
                    {
                        putaway.PalletPosition.RowVersion++;
                        db.InwardPutaways.Remove(putaway);
                    }
                }

                transaction.Lines.Remove(duplicate);
                db.InwardTransactionLines.Remove(duplicate);
            }
        }
    }

    private async Task ReleasePendingAsync(IReadOnlyList<InwardTransactionLine> lines, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            var oldPending = line.Putaways.Where(x => !x.IsConfirmed).ToList();
            foreach (var position in oldPending.Select(x => x.PalletPosition).DistinctBy(x => x.Id))
            {
                position.RowVersion++;
            }

            db.InwardPutaways.RemoveRange(oldPending);
            foreach (var putaway in oldPending)
            {
                line.Putaways.Remove(putaway);
            }

            var confirmedQuantity = line.Putaways.Where(x => x.IsConfirmed).Sum(x => x.QuantityBoxes);
            line.AllocatedQuantityBoxes = confirmedQuantity;
            line.Status = confirmedQuantity >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : confirmedQuantity > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new InvalidOperationException(
                "A location changed while the put-away was being refreshed. Reload the GRN and refresh again.");
        }
    }

    private async Task RefreshLineAsync(
        InwardTransactionLine line,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        var confirmedQuantity = line.Putaways.Where(x => x.IsConfirmed).Sum(x => x.QuantityBoxes);
        var quantityToAllocate = Math.Max(0, line.RequestedQuantityBoxes - confirmedQuantity);

        // Same retry-on-conflict shape as ReallocateInwardHandler: a location's RowVersion moves whenever anyone
        // reserves or releases space there, so another user's put-away landing on one of these locations while this
        // runs is a normal event, not a failure. Reload the touched locations and allocate this line again rather than
        // sending the user back to click Refresh once more.
        // Bounded on purpose (3 attempts): endless retries against a hot location could pile up waiting requests. The
        // short growing pause keeps competing refreshes from colliding in lockstep.
        // A material deactivated after the GRN was uploaded must not receive locations. Read the flag straight from the
        // database: the circuit's long-lived context may still hold the Material as it was when the GRN was opened.
        var materialActive = await db.Materials.AsNoTracking()
            .Where(m => m.Id == line.MaterialId)
            .Select(m => m.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        for (var attempt = 1; attempt <= MaxAllocationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allocation = quantityToAllocate > 0 && !materialActive
                ? new InwardAllocationResult(AllocationStatus.Failed, 0, [], "Material code not found in Material Master.")
                : quantityToAllocate > 0
                ? await allocator.AllocateAsync(
                    line.MaterialId,
                    quantityToAllocate,
                    line.MfgMonth,
                    warehouseId,
                    cancellationToken: cancellationToken)
                : new InwardAllocationResult(AllocationStatus.Fulfilled, 0, [], null);

            var added = new List<InwardPutaway>();
            foreach (var suggestion in allocation.Lines)
            {
                var position = await db.PalletPositions
                    .FirstAsync(x => x.LocationCode == suggestion.LocationCode, cancellationToken);
                position.RowVersion++;
                var putaway = new InwardPutaway
                {
                    PalletPositionId = position.Id,
                    PalletPosition = position,
                    QuantityBoxes = suggestion.QuantityBoxes,
                    AllocationReason = suggestion.Reason,
                    IsConfirmed = false
                };
                line.Putaways.Add(putaway);
                added.Add(putaway);
            }

            line.AllocatedQuantityBoxes = confirmedQuantity + allocation.AllocatedQuantityBoxes;
            line.Status = line.AllocatedQuantityBoxes >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : line.AllocatedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
            line.Remarks = line.Status == AllocationStatus.Fulfilled ? null : allocation.Remarks;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Remove() on a still-Added (never persisted) entity detaches it cleanly - see ReallocateInwardHandler.
                foreach (var putaway in added)
                {
                    db.InwardPutaways.Remove(putaway);
                }

                line.AllocatedQuantityBoxes = confirmedQuantity;
                foreach (var entry in db.ChangeTracker.Entries<PalletPosition>().Where(e => e.State == EntityState.Modified).ToList())
                {
                    await entry.ReloadAsync(cancellationToken);
                }

                await Task.Delay(Random.Shared.Next(10, 60) * attempt, cancellationToken);
            }
        }

        db.ChangeTracker.Clear();
        throw new InvalidOperationException(
            "A location changed while the put-away was being refreshed. Reload the GRN and refresh again.");
    }
}
