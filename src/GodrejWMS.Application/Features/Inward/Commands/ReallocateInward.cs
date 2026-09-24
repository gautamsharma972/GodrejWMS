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
/// Re-runs the put-away allocation engine for every still-short line of a pending GRN (Partial or
/// Failed - i.e. <see cref="InwardTransactionLine.RequestedQuantityBoxes"/> not yet fully covered
/// by <see cref="InwardTransactionLine.AllocatedQuantityBoxes"/>), against *current* warehouse
/// availability. Locations that were full when the GRN first came in (e.g. because of another
/// GRN's since-rejected/confirmed reservation) can now be tried again, without re-submitting the
/// whole GRN from scratch. Already-allocated lines and already-confirmed put-aways are untouched;
/// this only ever adds new reservations to cover the remaining gap.
/// </summary>
public sealed record ReallocateInwardCommand(int TransactionId) : IRequest<InwardResultDto>;

public sealed class ReallocateInwardValidator : AbstractValidator<ReallocateInwardCommand>
{
    public ReallocateInwardValidator()
    {
        RuleFor(x => x.TransactionId).GreaterThan(0);
    }
}

public sealed class ReallocateInwardHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator) : IRequestHandler<ReallocateInwardCommand, InwardResultDto>
{
    private const int MaxAllocationAttempts = 3;

    public async Task<InwardResultDto> Handle(ReallocateInwardCommand request, CancellationToken cancellationToken)
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
            throw new InvalidOperationException("This GRN has been rejected and can't be reallocated.");
        }

        var shortLines = transaction.Lines.Where(l => l.Status != AllocationStatus.Fulfilled).ToList();
        if (shortLines.Count == 0)
        {
            throw new InvalidOperationException("Every line on this GRN is already fully allocated.");
        }

        foreach (var line in shortLines)
        {
            var remaining = line.RequestedQuantityBoxes - line.AllocatedQuantityBoxes;
            if (remaining <= 0)
            {
                continue;
            }

            await ReallocateLineAsync(line, remaining, cancellationToken);
        }

        return InwardResultMapper.ToResult(transaction);
    }

    /// <summary>
    /// Same retry-on-concurrency-conflict shape as <c>SubmitInwardHandler.AllocateAndReserveLineAsync</c>
    /// (one line's own <c>SaveChangesAsync</c>, retried independently so one line's conflict
    /// doesn't force redoing every other line) - see that method's remarks for why the atomicity
    /// this relies on is a standard relational-provider guarantee, not proven by an InMemory test.
    /// </summary>
    private async Task ReallocateLineAsync(InwardTransactionLine line, decimal remaining, CancellationToken cancellationToken)
    {
        var originalAllocated = line.AllocatedQuantityBoxes;

        for (var attempt = 1; attempt <= MaxAllocationAttempts; attempt++)
        {
            var allocation = await allocator.AllocateAsync(line.MaterialId, remaining, line.MfgMonth, cancellationToken: cancellationToken);

            var addedPutaways = new List<InwardPutaway>();
            foreach (var putaway in allocation.Lines)
            {
                var position = await db.PalletPositions
                    .FirstAsync(p => p.LocationCode == putaway.LocationCode, cancellationToken);
                position.RowVersion++;

                var reservedPutaway = new InwardPutaway
                {
                    PalletPositionId = position.Id,
                    QuantityBoxes = putaway.QuantityBoxes,
                    AllocationReason = putaway.Reason,
                    IsConfirmed = false
                };

                line.Putaways.Add(reservedPutaway);
                addedPutaways.Add(reservedPutaway);
            }

            line.AllocatedQuantityBoxes = originalAllocated + allocation.AllocatedQuantityBoxes;
            line.Status = line.AllocatedQuantityBoxes >= line.RequestedQuantityBoxes
                ? AllocationStatus.Fulfilled
                : line.AllocatedQuantityBoxes > 0
                    ? AllocationStatus.Partial
                    : AllocationStatus.Failed;
            line.Remarks = line.Status == AllocationStatus.Fulfilled ? null : allocation.Remarks;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Remove() on a still-Added (never persisted) entity detaches it cleanly. The
                // alternative - removing it from line.Putaways first, then setting .Entry().State
                // - fights the required-FK navigation fixup EF runs when an entity leaves a
                // collection, which throws "InwardPutaway.Id is part of a key and cannot be
                // modified" instead of the clean detach this needs.
                foreach (var putaway in addedPutaways)
                {
                    db.InwardPutaways.Remove(putaway);
                }

                line.AllocatedQuantityBoxes = originalAllocated;

                foreach (var entry in db.ChangeTracker.Entries<PalletPosition>().Where(e => e.State == EntityState.Modified).ToList())
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            "Unable to reallocate this line because of high concurrent demand on the same location(s). Please retry.");
    }
}
