using GodrejWMS.Application.Common.Permissions;
using System.Text.Json;
using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

public sealed record PulloutPickSource(int PalletPositionId, string MfgMonthLabel, decimal QuantityBoxes);

[RequiresPermission(PermissionKeys.Pullout.Edit, PermissionKeys.Pullout.Pick)]
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
        // The reason is only required when the change overrides FIFO; the handler enforces that.
        RuleFor(x => x.Reason).MaximumLength(250);
    }
}

public sealed class ChangePulloutPickLocationHandler(IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow)
    : IRequestHandler<ChangePulloutPickLocationCommand, PulloutResultDto>
{
    public Task<PulloutResultDto> Handle(ChangePulloutPickLocationCommand request, CancellationToken cancellationToken) =>
        InwardSubmissionLogic.RunOrderAsync(uow, ct => RunAsync(request, ct), cancellationToken);

    private async Task<PulloutResultDto> RunAsync(ChangePulloutPickLocationCommand request, CancellationToken cancellationToken)
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
        // A posted pullout has already taken its picks out of stock: changing a pick source is a correction (the old
        // source gets its boxes back, the new one is taken from). An order still pending from before posting on upload
        // only has proposed picks, which are simply replaced.
        var posted = transaction.IsConfirmed;

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
        // Picks stay editable - even ones the picker already confirmed - until the pick-list itself is confirmed. A replaced pick comes
        // back unconfirmed, so the picker confirms the new source again.
        if (posted && (transaction.ConfirmedAt is not null || transaction.UploadHash is null))
            throw new InvalidOperationException("This pick-list has already been confirmed, so its picks can no longer be changed.");
        var replacedPickIds = picksToReplace.Select(p => p.Id).ToHashSet();
        decimal ReturningAt(int palletPositionId, int sourceMfgMonth) => posted
            ? picksToReplace.Where(p => p.PalletPositionId == palletPositionId && p.MfgMonth == sourceMfgMonth).Sum(p => p.QuantityBoxes)
            : 0m;

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

            var returning = ReturningAt(source.PalletPositionId, source.MfgMonth);
            batchesBySource.TryGetValue((source.PalletPositionId, source.MfgMonth), out var batch);
            if (batch is null && returning <= 0)
                throw new InvalidOperationException(
                    $"Location {position.LocationCode} does not contain this SKU and PKM as available good stock.");

            var available = (batch?.QuantityBoxes ?? 0m) + returning
                - allocatedByOtherPicks.GetValueOrDefault((source.PalletPositionId, source.MfgMonth));
            if (source.QuantityBoxes > available)
                throw new InvalidOperationException(
                    $"Location {position.LocationCode} has only {Math.Max(0, available):0.###} available box(es); "
                    + $"{source.QuantityBoxes:0.###} were selected.");
        }

        // FIFO override: taking a PKM newer than the oldest one still available skips older stock (typically because it is
        // damaged). That is allowed, but only with a reason; a change that keeps FIFO needs none.
        var oldestAvailable = await db.StockBatches.AsNoTracking()
            .Where(b => b.MaterialId == line.MaterialId && b.StockSubtypeId == LocationSubtypeIds.Good && b.QuantityBoxes > 0
                && b.PalletPosition.IsActive && b.PalletPosition.LocationSubtypeId == LocationSubtypeIds.Good
                && b.PalletPosition.Rack.WarehouseId == transaction.WarehouseId)
            .MinAsync(b => (int?)b.MfgMonth, cancellationToken);
        // Boxes a posted pick hands back count as available too (its cell may be empty right now).
        var candidates = picksToReplace.Where(_ => posted).Select(p => (int?)p.MfgMonth).Append(oldestAvailable).Where(m => m.HasValue).Select(m => m!.Value).ToList();
        var oldest = candidates.Count > 0 ? candidates.Min() : int.MaxValue;
        var overridesFifo = parsedSources.Any(source => source.MfgMonth > oldest);
        var reason = (request.Reason ?? string.Empty).Trim();
        if (overridesFifo && reason.Length == 0)
            throw new InvalidOperationException(
                $"This takes a newer PKM than the oldest one available ({MfgMonthParser.Format(oldest)}), which overrides FIFO. Enter a reason for the override.");
        var replacementPreviewPicks = parsedSources.Select(source => new PulloutPickDto(
            positions[source.PalletPositionId].LocationCode,
            MfgMonthParser.Format(source.MfgMonth),
            source.QuantityBoxes,
            reason.Length == 0 ? null : reason)).ToList();

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

        if (posted)
        {
            var positionIdsToPost = picksToReplace.Select(p => p.PalletPositionId)
                .Concat(parsedSources.Select(s => s.PalletPositionId)).Distinct().ToList();
            var tracked = await db.PalletPositions.Include(p => p.Rack)
                .Where(p => positionIdsToPost.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
            var changes = picksToReplace
                .Select(p => new StockChange(line.MaterialId, p.MfgMonth, LocationSubtypeIds.Good, tracked[p.PalletPositionId], p.QuantityBoxes))
                .Concat(parsedSources.Select(s => new StockChange(line.MaterialId, s.MfgMonth, LocationSubtypeIds.Good, tracked[s.PalletPositionId], -s.QuantityBoxes)))
                .ToList();
            await posting.ApplyAsync(
                new StockPostingContext(StockLedgerEntryTypes.PickChange, "PICK", transaction.ReferenceNumber,
                    reason.Length == 0 ? "Pick location changed" : $"Pick location changed: {reason}"),
                changes, cancellationToken);
        }

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
                OverrideReason = reason.Length == 0 ? null : reason
            });
        }
        line.PickedQuantityBoxes = newLineTotal;
        line.Status = newStatus;
        line.Remarks = newRemarks;

        // The old sources no longer have this pick waiting: a virtual one that is now empty is free for reuse.
        if (posted)
        {
            await posting.ReleaseEmptyVirtualLocationsAsync(picksToReplace.Select(p => p.PalletPosition), cancellationToken);
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
