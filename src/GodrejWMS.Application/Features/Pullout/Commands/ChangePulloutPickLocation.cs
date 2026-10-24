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

        var line = transaction.Lines.SingleOrDefault(l => l.Material.MaterialNumber == request.MaterialNumber)
            ?? throw new InvalidOperationException("The selected pullout line could not be found.");
        var pick = line.Picks.SingleOrDefault(p => p.MfgMonth == mfgMonth
                && p.PalletPosition.LocationCode == request.CurrentLocationCode)
            ?? throw new InvalidOperationException("The selected saved pick could not be found.");

        var selectedQuantity = parsedSources.Sum(source => source.QuantityBoxes);
        if (selectedQuantity != pick.QuantityBoxes)
            throw new InvalidOperationException(
                $"Selected sources total {selectedQuantity:0.###} box(es); exactly {pick.QuantityBoxes:0.###} are required.");

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

        var allocatedByOtherPicks = transaction.Lines
            .SelectMany(transactionLine => transactionLine.Picks)
            .Where(otherPick => otherPick.Id != pick.Id && otherPick.PulloutTransactionLine.MaterialId == line.MaterialId)
            .GroupBy(otherPick => (otherPick.PalletPositionId, otherPick.MfgMonth))
            .ToDictionary(group => group.Key, group => group.Sum(otherPick => otherPick.QuantityBoxes));

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

        var preview = DeserializePreview(transaction);
        var changed = false;
        var updatedLines = preview.Lines.Select(previewLine =>
        {
            if (previewLine.MaterialNumber != request.MaterialNumber)
                return previewLine;

            var updatedPicks = new List<PulloutPickDto>();
            foreach (var previewPick in previewLine.Picks)
            {
                if (!changed
                    && previewPick.LocationCode.Equals(request.CurrentLocationCode, StringComparison.OrdinalIgnoreCase)
                    && previewPick.MfgMonthLabel.Equals(request.MfgMonthLabel, StringComparison.OrdinalIgnoreCase))
                {
                    updatedPicks.AddRange(replacementPreviewPicks);
                    changed = true;
                }
                else
                {
                    updatedPicks.Add(previewPick);
                }
            }

            return previewLine with { Picks = updatedPicks };
        }).ToList();

        if (!changed)
            throw new InvalidOperationException("The selected pick is no longer present in the saved allocation.");

        line.Picks.Remove(pick);
        db.PulloutPicks.Remove(pick);
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
