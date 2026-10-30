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
public sealed record RefreshInwardPutawayCommand(int TransactionId) : IRequest<InwardResultDto>;

public sealed class RefreshInwardPutawayValidator : AbstractValidator<RefreshInwardPutawayCommand>
{
    public RefreshInwardPutawayValidator() => RuleFor(x => x.TransactionId).GreaterThan(0);
}

public sealed class RefreshInwardPutawayHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator) : IRequestHandler<RefreshInwardPutawayCommand, InwardResultDto>
{
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

        foreach (var line in transaction.Lines.OrderBy(x => x.Id))
        {
            await RefreshLineAsync(line, transaction.WarehouseId, cancellationToken);
        }

        return InwardResultMapper.ToResult(transaction);
    }

    private async Task RefreshLineAsync(
        InwardTransactionLine line,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        var confirmedQuantity = line.Putaways.Where(x => x.IsConfirmed).Sum(x => x.QuantityBoxes);
        var quantityToAllocate = Math.Max(0, line.RequestedQuantityBoxes - confirmedQuantity);
        var oldPending = line.Putaways.Where(x => !x.IsConfirmed).ToList();

        var allocation = quantityToAllocate > 0
            ? await allocator.AllocateAsync(
                line.MaterialId,
                quantityToAllocate,
                line.MfgMonth,
                warehouseId,
                excludeInwardTransactionLineId: line.Id,
                cancellationToken: cancellationToken)
            : new InwardAllocationResult(AllocationStatus.Fulfilled, 0, [], null);

        foreach (var position in oldPending.Select(x => x.PalletPosition).DistinctBy(x => x.Id))
        {
            position.RowVersion++;
        }
        db.InwardPutaways.RemoveRange(oldPending);

        foreach (var suggestion in allocation.Lines)
        {
            var position = await db.PalletPositions
                .FirstAsync(x => x.LocationCode == suggestion.LocationCode, cancellationToken);
            position.RowVersion++;
            line.Putaways.Add(new InwardPutaway
            {
                PalletPositionId = position.Id,
                PalletPosition = position,
                QuantityBoxes = suggestion.QuantityBoxes,
                AllocationReason = suggestion.Reason,
                IsConfirmed = false
            });
        }

        line.AllocatedQuantityBoxes = confirmedQuantity + allocation.AllocatedQuantityBoxes;
        line.Status = line.AllocatedQuantityBoxes >= line.RequestedQuantityBoxes
            ? AllocationStatus.Fulfilled
            : line.AllocatedQuantityBoxes > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
        line.Remarks = line.Status == AllocationStatus.Fulfilled ? null : allocation.Remarks;

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
}
