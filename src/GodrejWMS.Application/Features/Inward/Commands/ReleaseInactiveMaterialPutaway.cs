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
/// Refresh Put-away for a GRN that is already posted: for every line whose material code was made inactive after the
/// upload, the stock is taken back out of the locations it was placed in (ledger reversal, virtual locations released)
/// and the line is left without those locations. Nothing is re-allocated for an inactive material. Put-aways the picker has
/// already submitted, and lines of active materials, are never touched. Boxes already picked or moved are not reversed; only what is still in place is taken back.
/// </summary>
[RequiresPermission(PermissionKeys.Inward.Edit, PermissionKeys.Inward.Putaway, PermissionKeys.Inward.Reallocate)]
public sealed record ReleaseInactiveMaterialPutawayCommand(int TransactionId) : IRequest<InwardResultDto>;

public sealed class ReleaseInactiveMaterialPutawayValidator : AbstractValidator<ReleaseInactiveMaterialPutawayCommand>
{
    public ReleaseInactiveMaterialPutawayValidator() => RuleFor(x => x.TransactionId).GreaterThan(0);
}

public sealed class ReleaseInactiveMaterialPutawayHandler(
    IApplicationDbContext db,
    IStockPostingService posting,
    IUnitOfWork uow) : IRequestHandler<ReleaseInactiveMaterialPutawayCommand, InwardResultDto>
{
    public Task<InwardResultDto> Handle(ReleaseInactiveMaterialPutawayCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<InwardResultDto> RunAsync(ReleaseInactiveMaterialPutawayCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Putaways).ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected, or every one has already been submitted by the picker, so there is nothing to release.");
        }

        // Read the flag from the database: the circuit's long-lived context may still hold the Material as it was when opened.
        var materialIds = transaction.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var inactiveIds = await db.Materials.AsNoTracking()
            .Where(m => materialIds.Contains(m.Id) && !m.IsActive)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        // Put-aways the picker has already submitted stay as they are; only untouched (or still unconfirmed) ones are released.
        static bool Releasable(InwardPutaway p) => !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation is null;
        var lines = transaction.Lines.Where(l => inactiveIds.Contains(l.MaterialId) && l.Putaways.Any(Releasable)).ToList();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("No inactive material in this GRN holds a location, or every one has already been submitted by the picker, so there is nothing to release.");
        }

        var posted = lines
            .SelectMany(l => l.Putaways.Where(p => p.IsConfirmed && Releasable(p) && l.MfgMonth is not null).Select(p => (Line: l, Putaway: p)))
            .ToList();
        if (posted.Count > 0)
        {
            // Takes back what is still in place; boxes already picked or moved cannot be reversed and stay as history.
            var plan = await PostedStockReversal.PlanAsync(
                db,
                posted.Select(x => new StockReversalItem(x.Line.MaterialId, x.Line.MfgMonth!.Value, x.Putaway.PalletPosition, x.Putaway.QuantityBoxes)).ToList(),
                cancellationToken);
            if (plan.Changes.Count > 0)
            {
                await posting.ApplyAsync(
                    new StockPostingContext(StockLedgerEntryTypes.Reversal, "GRN", transaction.ReferenceNumber,
                        "Material made inactive after upload - put-away released"),
                    plan.Changes,
                    cancellationToken);
            }
        }

        foreach (var line in lines)
        {
            foreach (var putaway in line.Putaways.Where(Releasable).ToList())
            {
                putaway.PalletPosition.RowVersion++;
                db.InwardPutaways.Remove(putaway);
                line.Putaways.Remove(putaway);
            }

            line.AllocatedQuantityBoxes = line.Putaways.Sum(p => p.QuantityBoxes);
            line.Status = line.AllocatedQuantityBoxes <= 0 ? AllocationStatus.Failed : AllocationStatus.Partial;
            line.Remarks = "Material code was made inactive after upload - put-away released, no location allocated.";
        }

        await db.SaveChangesAsync(cancellationToken);
        return InwardResultMapper.ToResult(transaction);
    }
}
