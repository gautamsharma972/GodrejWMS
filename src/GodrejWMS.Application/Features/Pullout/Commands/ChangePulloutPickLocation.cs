using System.Text.Json;
using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

public sealed record PulloutPickSource(int PalletPositionId, string MfgMonthLabel, decimal QuantityBoxes);

public sealed record ChangePulloutPickLocationCommand(
    string ReferenceNumber,
    long MaterialNumber,
    string CurrentLocationCode,
    string MfgMonthLabel,
    IReadOnlyList<PulloutPickSource> Sources,
    string Reason) : IRequest<PulloutResultDto>;

public sealed class ChangePulloutPickLocationValidator : AbstractValidator<ChangePulloutPickLocationCommand>
{
    public ChangePulloutPickLocationValidator()
    {
        RuleFor(x => x.ReferenceNumber).NotEmpty();
        RuleFor(x => x.MaterialNumber).GreaterThan(0);
        RuleFor(x => x.CurrentLocationCode).NotEmpty();
        RuleFor(x => x.MfgMonthLabel).NotEmpty();
        RuleFor(x => x.Sources).NotEmpty().WithMessage("Select at least one pick location.");
        RuleForEach(x => x.Sources).ChildRules(source =>
        {
            source.RuleFor(x => x.PalletPositionId).GreaterThan(0);
            source.RuleFor(x => x.MfgMonthLabel).NotEmpty();
            source.RuleFor(x => x.QuantityBoxes).GreaterThan(0);
        });
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when changing pick locations.")
            .MaximumLength(250);
    }
}

public sealed class ChangePulloutPickLocationHandler(IApplicationDbContext db)
    : IRequestHandler<ChangePulloutPickLocationCommand, PulloutResultDto>
{
    public async Task<PulloutResultDto> Handle(ChangePulloutPickLocationCommand request, CancellationToken cancellationToken)
    {
        if (!MfgMonthParser.TryParse(request.MfgMonthLabel, out var mfgMonth))
            throw new InvalidOperationException("The selected pick has an invalid PKM period.");

        var parsedSources = request.Sources.Select(source =>
        {
            if (!MfgMonthParser.TryParse(source.MfgMonthLabel, out var sourceMfgMonth))
                throw new InvalidOperationException($"PKM {source.MfgMonthLabel} is invalid.");
            return new ParsedSource(source.PalletPositionId, sourceMfgMonth, source.QuantityBoxes);
        }).ToList();

        if (parsedSources.GroupBy(source => (source.PalletPositionId, source.MfgMonth)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Each location and PKM can only be selected once.");

        var transaction = await db.PulloutTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.PalletPosition)
            .SingleOrDefaultAsync(t => t.ReferenceNumber == request.ReferenceNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.ReferenceNumber);

        await db.Entry(transaction).ReloadAsync(cancellationToken);
        if (transaction.IsRejected)
            throw new InvalidOperationException("This pullout was rejected and its pick locations can no longer be changed.");
        if (transaction.IsConfirmed)
            throw new InvalidOperationException("This pullout has already been confirmed and its pick locations can no longer be changed.");

        var line = transaction.Lines.FirstOrDefault(l =>
                l.Material.MaterialNumber == request.MaterialNumber &&
                l.Picks.Any(p => p.MfgMonth == mfgMonth
                    && p.PalletPosition.LocationCode == request.CurrentLocationCode))
            ?? throw new InvalidOperationException("The selected pullout line could not be found.");
        var picksToReplace = line.Picks
            .Where(p => p.MfgMonth == mfgMonth
                && p.PalletPosition.LocationCode == request.CurrentLocationCode)
            .ToList();
        if (picksToReplace.Count == 0)
            throw new InvalidOperationException("The selected saved pick could not be found.");
        var replacedPickIds = picksToReplace.Select(p => p.Id).ToHashSet();

        // The replacement total no longer has to exactly match the pick being replaced - real-time
        // picking means the actual available/picked quantity at a location often differs from the
        // original FIFO suggestion. It only has to still fit within what the line as a whole was
        // requested: the sum of every OTHER pick already on this line, plus this replacement,
        // can't exceed RequestedQuantityBoxes.
        var selectedQuantity = parsedSources.Sum(source => source.QuantityBoxes);
        var otherPicksTotal = line.Picks.Where(p => !replacedPickIds.Contains(p.Id)).Sum(p => p.QuantityBoxes);
        var newLineTotal = otherPicksTotal + selectedQuantity;
        if (newLineTotal > line.RequestedQuantityBoxes)
            throw new InvalidOperationException(
                $"Selected sources total {selectedQuantity:0.###} box(es), which would bring this line to "
                + $"{newLineTotal:0.###} - more than the {line.RequestedQuantityBoxes:0.###} requested.");

        var positionIds = parsedSources.Select(source => source.PalletPositionId).Distinct().ToList();
        var positions = await db.PalletPositions.AsNoTracking()
            .Where(position => positionIds.Contains(position.Id))
            .ToDictionaryAsync(position => position.Id, cancellationToken);
        if (positions.Count != positionIds.Count)
            throw new InvalidOperationException("One or more selected pick locations no longer exist.");

        var batches = await db.StockBatches.AsNoTracking()
            .Where(batch => positionIds.Contains(batch.PalletPositionId)
                && batch.MaterialId == line.MaterialId
                && batch.StockSubtypeId == LocationSubtypeIds.Good
                && batch.QuantityBoxes > 0)
            .ToListAsync(cancellationToken);
        var batchesBySource = batches.ToDictionary(batch => (batch.PalletPositionId, batch.MfgMonth));

        var allocatedByOtherPicks = await db.PulloutPicks.AsNoTracking()
            .Where(otherPick => !replacedPickIds.Contains(otherPick.Id)
                && otherPick.PulloutTransactionLine.MaterialId == line.MaterialId
                && otherPick.PulloutTransactionLine.PulloutTransaction.WarehouseId == transaction.WarehouseId
                && !otherPick.PulloutTransactionLine.PulloutTransaction.IsConfirmed
                && !otherPick.PulloutTransactionLine.PulloutTransaction.IsRejected)
            .GroupBy(otherPick => new { otherPick.PalletPositionId, otherPick.MfgMonth })
            .Select(group => new { group.Key.PalletPositionId, group.Key.MfgMonth, Quantity = group.Sum(p => p.QuantityBoxes) })
            .ToDictionaryAsync(x => (x.PalletPositionId, x.MfgMonth), x => x.Quantity, cancellationToken);

        foreach (var source in parsedSources)
        {
            var position = positions[source.PalletPositionId];
            if (!position.IsActive || position.LocationSubtypeId != LocationSubtypeIds.Good)
                throw new InvalidOperationException($"Location {position.LocationCode} is not an active good-stock picking location.");

            if (!batchesBySource.TryGetValue((source.PalletPositionId, source.MfgMonth), out var batch))
                throw new InvalidOperationException(
                    $"Location {position.LocationCode} does not contain this SKU and PKM as available good stock.");

            var available = batch.QuantityBoxes
                - allocatedByOtherPicks.GetValueOrDefault((source.PalletPositionId, source.MfgMonth));
            if (source.QuantityBoxes > available)
                throw new InvalidOperationException(
                    $"Location {position.LocationCode} has only {Math.Max(0, available):0.###} available box(es); "
                    + $"{source.QuantityBoxes:0.###} were selected.");
        }

        var reason = request.Reason.Trim();
        var replacementPreviewPicks = parsedSources.Select(source => new PulloutPickDto(
            positions[source.PalletPositionId].LocationCode,
            MfgMonthParser.Format(source.MfgMonth),
            source.QuantityBoxes,
            reason)).ToList();

        // The line's own picked-quantity/status/remarks need to reflect the new total too - the
        // same classification SubmitPulloutHandler uses when it first allocates a line.
        var newStatus = newLineTotal == line.RequestedQuantityBoxes
            ? AllocationStatus.Fulfilled
            : newLineTotal > 0 ? AllocationStatus.Partial : AllocationStatus.Failed;
        var newRemarks = newStatus == AllocationStatus.Fulfilled
            ? null
            : $"Insufficient good stock - {line.RequestedQuantityBoxes - newLineTotal:0.###} box(es) short across all pickable locations.";

        var preview = DeserializePreview(transaction);
        var changed = false;
        var updatedLines = preview.Lines.Select(previewLine =>
        {
            if (changed || previewLine.MaterialNumber != request.MaterialNumber)
                return previewLine;

            var updatedPicks = new List<PulloutPickDto>();
            var replacementAdded = false;
            foreach (var previewPick in previewLine.Picks)
            {
                if (previewPick.LocationCode.Equals(request.CurrentLocationCode, StringComparison.OrdinalIgnoreCase)
                    && previewPick.MfgMonthLabel.Equals(request.MfgMonthLabel, StringComparison.OrdinalIgnoreCase))
                {
                    if (!replacementAdded)
                    {
                        updatedPicks.AddRange(replacementPreviewPicks);
                        replacementAdded = true;
                    }
                }
                else
                {
                    updatedPicks.Add(previewPick);
                }
            }

            changed = replacementAdded;
            if (!replacementAdded)
                return previewLine;

            return previewLine with { Picks = updatedPicks, PickedQuantityBoxes = newLineTotal, Status = newStatus, Remarks = newRemarks };
        }).ToList();

        if (!changed)
            throw new InvalidOperationException("The selected pick is no longer present in the saved allocation.");

        foreach (var pick in picksToReplace)
            line.Picks.Remove(pick);
        db.PulloutPicks.RemoveRange(picksToReplace);
        foreach (var source in parsedSources)
        {
            line.Picks.Add(new PulloutPick
            {
                PalletPositionId = source.PalletPositionId,
                MfgMonth = source.MfgMonth,
                QuantityBoxes = source.QuantityBoxes,
                OverrideReason = reason
            });
        }
        line.PickedQuantityBoxes = newLineTotal;
        line.Status = newStatus;
        line.Remarks = newRemarks;

        var updated = preview with { Lines = updatedLines };
        transaction.PreviewJson = JsonSerializer.Serialize(updated);
        transaction.RowVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private static PulloutResultDto DeserializePreview(PulloutTransaction transaction) =>
        !string.IsNullOrWhiteSpace(transaction.PreviewJson)
            ? JsonSerializer.Deserialize<PulloutResultDto>(transaction.PreviewJson)!
            : throw new InvalidOperationException("The saved pullout allocation is unavailable. Refresh the picks and try again.");

    private sealed record ParsedSource(int PalletPositionId, int MfgMonth, decimal QuantityBoxes);
}
