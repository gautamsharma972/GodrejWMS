using FluentValidation;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>Allocates the uploaded lines a GRN could not save because their material code was not yet in the Material
/// Master. Once the material has been added, each such line becomes a normal GRN line and has its put-away locations
/// reserved, and leaves the GRN's "could not be allocated" list. Lines whose code is still unknown stay listed.
/// <see cref="FallbackMfgMonth"/> (yyyymm) stands in for the Mfg Month of lines saved before it was recorded; it is
/// ignored for a no-PKM GRN.</summary>
public sealed record RetryInwardUnsavedLinesCommand(int TransactionId, int? FallbackMfgMonth = null) : IRequest<RetryInwardUnsavedLinesResult>;

public sealed record RetryInwardUnsavedLinesResult(int PlacedLines, decimal PlacedBoxes, int StillMissingLines, int NeedsPkmLines);

public sealed class RetryInwardUnsavedLinesValidator : AbstractValidator<RetryInwardUnsavedLinesCommand>
{
    public RetryInwardUnsavedLinesValidator()
    {
        RuleFor(x => x.TransactionId).GreaterThan(0);
        RuleFor(x => x.FallbackMfgMonth!.Value)
            .Must(m => m / 100 is >= 2000 and <= 2100 && m % 100 is >= 1 and <= 12)
            .When(x => x.FallbackMfgMonth.HasValue)
            .WithMessage("Mfg Month is not valid.");
    }
}

public sealed class RetryInwardUnsavedLinesHandler(
    IApplicationDbContext db,
    IInwardPlacementService placement,
    IUnitOfWork uow) : IRequestHandler<RetryInwardUnsavedLinesCommand, RetryInwardUnsavedLinesResult>
{
    public Task<RetryInwardUnsavedLinesResult> Handle(RetryInwardUnsavedLinesCommand request, CancellationToken cancellationToken) =>
        InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);

    private async Task<RetryInwardUnsavedLinesResult> RunAsync(RetryInwardUnsavedLinesCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected and its lines can't be allocated.");
        }

        var unsaved = await db.InwardUnsavedLines
            .Where(u => u.InwardTransactionId == transaction.Id && u.Kind == InwardUnsavedLineKind.UnknownMaterial)
            .OrderBy(u => u.SourceRow ?? int.MaxValue).ThenBy(u => u.Id)
            .ToListAsync(cancellationToken);
        if (unsaved.Count == 0)
        {
            throw new InvalidOperationException("This GRN has no unallocated uploaded lines to retry.");
        }

        var codes = unsaved
            .Select(u => long.TryParse(u.CodeText.Trim(), out var code) ? code : (long?)null)
            .Where(c => c.HasValue).Select(c => c!.Value).Distinct().ToList();
        var materialsByCode = await db.Materials
            .Where(m => m.IsActive && codes.Contains(m.MaterialNumber))
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);

        var placedLines = 0;
        var placedBoxes = 0m;
        var stillMissing = 0;
        var needsPkm = 0;

        foreach (var line in unsaved)
        {
            if (!long.TryParse(line.CodeText.Trim(), out var code) || !materialsByCode.TryGetValue(code, out var material))
            {
                stillMissing++;
                continue;
            }

            int? mfgMonth = null;
            if (!transaction.IsPkmless)
            {
                mfgMonth = line.MfgMonth ?? request.FallbackMfgMonth;
                if (mfgMonth is null)
                {
                    needsPkm++;
                    continue;
                }
            }

            // Removed in the same SaveChanges that adds the new line, so the quantity is never counted twice or lost.
            db.InwardUnsavedLines.Remove(line);
            var placed = await placement.PlaceLineAsync(
                transaction, material, line.QuantityBoxes, mfgMonth, transaction.WarehouseId, cancellationToken);

            placedLines++;
            placedBoxes += placed.Allocation.AllocatedQuantityBoxes;
        }

        return new RetryInwardUnsavedLinesResult(placedLines, placedBoxes, stillMissing, needsPkm);
    }
}
