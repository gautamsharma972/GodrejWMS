using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

public sealed record ImportInwardPutawayLocationsCommand(
    int InwardTransactionId,
    Stream FileStream,
    string Reason) : IRequest<ImportInwardPutawayLocationsResult>;

public sealed record ImportInwardPutawayLocationsResult(int UpdatedLocations);

public sealed class ImportInwardPutawayLocationsValidator : AbstractValidator<ImportInwardPutawayLocationsCommand>
{
    public ImportInwardPutawayLocationsValidator()
    {
        RuleFor(x => x.InwardTransactionId).GreaterThan(0);
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("A reason is required when changing put-away locations from Excel.")
            .MaximumLength(250);
    }
}

/// <summary>
/// Applies pallet-position edits from a GRN's own download workbook. The workbook is treated as a
/// complete final plan: every immutable value and every destination is validated before tracked
/// put-aways are changed, followed by one SaveChanges call.
/// </summary>
public sealed class ImportInwardPutawayLocationsHandler(IApplicationDbContext db, IExcelService excel)
    : IRequestHandler<ImportInwardPutawayLocationsCommand, ImportInwardPutawayLocationsResult>
{
    public async Task<ImportInwardPutawayLocationsResult> Handle(
        ImportInwardPutawayLocationsCommand request,
        CancellationToken cancellationToken)
    {
        var rows = excel.ReadInwardPutawayReassignment(request.FileStream);
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines)
                .ThenInclude(l => l.Material)
            .Include(t => t.Lines)
                .ThenInclude(l => l.Putaways)
                    .ThenInclude(p => p.PalletPosition)
            .FirstOrDefaultAsync(t => t.Id == request.InwardTransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.InwardTransactionId);

        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("A rejected GRN cannot be changed.");
        }

        var putaways = transaction.Lines.SelectMany(line => line.Putaways).OrderBy(p => p.Id).ToList();
        if (putaways.Count == 0)
        {
            throw new InvalidOperationException("This GRN has no put-away reservations to update.");
        }

        var identifiedRows = rows.Where(row => row.PutawayId.HasValue).ToList();
        if (identifiedRows.Count != putaways.Count)
        {
            throw new InvalidOperationException(
                "The workbook does not contain the complete put-away plan for this GRN. Download it again and re-upload the complete file.");
        }

        var duplicateId = identifiedRows
            .GroupBy(row => row.PutawayId!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"Put-away ID {duplicateId.Key} appears more than once in the workbook.");
        }

        var putawaysById = putaways.ToDictionary(p => p.Id);
        var targetCodes = new Dictionary<int, (string Code, int RowNumber)>();
        foreach (var row in identifiedRows)
        {
            if (!row.ReferenceNumber.Equals(transaction.ReferenceNumber, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: GRN Reference {row.ReferenceNumber} does not match {transaction.ReferenceNumber}.");
            }

            if (!putawaysById.TryGetValue(row.PutawayId!.Value, out var putaway))
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: Put-away ID {row.PutawayId} does not belong to this GRN.");
            }

            var line = putaway.InwardTransactionLine;
            if (row.MaterialNumber != line.Material.MaterialNumber)
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: SKU mismatch for put-away ID {putaway.Id}. Expected {line.Material.MaterialNumber}.");
            }

            if (!MfgMonthParser.TryParse(row.MfgMonthLabel, out var mfgMonth) || mfgMonth != line.MfgMonth)
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: PKM mismatch for SKU {line.Material.MaterialNumber}. Expected {MfgMonthParser.Format(line.MfgMonth)}.");
            }

            if (row.RequestedQuantityBoxes != line.RequestedQuantityBoxes
                || row.AllocatedQuantityBoxes != line.AllocatedQuantityBoxes
                || row.QuantityBoxes != putaway.QuantityBoxes)
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: quantities do not match the current GRN allocation. Download the workbook again before editing locations.");
            }

            if (string.IsNullOrWhiteSpace(row.LocationCode))
            {
                throw new InvalidOperationException($"Row {row.RowNumber}: Pallet Position is required.");
            }

            var expectedStatus = putaway.IsConfirmed ? "Confirmed" : "Reserved";
            if (!row.PutawayStatus.Equals(expectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: Put-away Status must remain {expectedStatus}.");
            }

            var targetCode = row.LocationCode.Trim().ToUpperInvariant();
            if (putaway.IsConfirmed && !targetCode.Equals(putaway.PalletPosition.LocationCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Row {row.RowNumber}: confirmed put-away {putaway.PalletPosition.LocationCode} can no longer be moved.");
            }

            targetCodes[putaway.Id] = (targetCode, row.RowNumber);
        }

        var workbookIds = identifiedRows.Select(row => row.PutawayId!.Value).ToHashSet();
        var missingId = putawaysById.Keys.FirstOrDefault(id => !workbookIds.Contains(id));
        if (missingId != 0)
        {
            throw new InvalidOperationException(
                $"Put-away ID {missingId} is missing from the workbook. Download the GRN Excel again and keep every row.");
        }

        var pendingPutaways = putaways.Where(p => !p.IsConfirmed).ToList();
        if (pendingPutaways.Count == 0)
        {
            throw new InvalidOperationException("All put-away locations in this GRN are already confirmed and cannot be changed.");
        }

        var distinctTargetCodes = pendingPutaways.Select(p => targetCodes[p.Id].Code).Distinct().ToArray();
        var positions = await db.PalletPositions
            .Include(p => p.StockBatches)
            .Where(p => distinctTargetCodes.Contains(p.LocationCode))
            .ToListAsync(cancellationToken);
        var positionsByCode = positions.ToDictionary(p => p.LocationCode, StringComparer.OrdinalIgnoreCase);

        foreach (var putaway in pendingPutaways)
        {
            var target = targetCodes[putaway.Id];
            if (!positionsByCode.ContainsKey(target.Code))
            {
                throw new InvalidOperationException($"Row {target.RowNumber}: location {target.Code} was not found.");
            }
        }

        var selectedIds = pendingPutaways.Select(p => p.Id).ToArray();
        var targetPositionIds = positions.Select(p => p.Id).ToArray();
        var otherReservations = await db.InwardPutaways
            .AsNoTracking()
            .Where(p => !p.IsConfirmed
                        && !selectedIds.Contains(p.Id)
                        && targetPositionIds.Contains(p.PalletPositionId))
            .Select(p => new
            {
                p.PalletPositionId,
                p.QuantityBoxes,
                p.InwardTransactionLine.MaterialId
            })
            .ToListAsync(cancellationToken);

        var occupied = positions.ToDictionary(
            p => p.Id,
            p => p.StockBatches.Where(b => b.QuantityBoxes > 0).Sum(b => b.QuantityBoxes)
                 + otherReservations.Where(r => r.PalletPositionId == p.Id).Sum(r => r.QuantityBoxes));
        var materialIds = positions.ToDictionary(
            p => p.Id,
            p => p.StockBatches.Where(b => b.QuantityBoxes > 0).Select(b => b.MaterialId)
                .Concat(otherReservations.Where(r => r.PalletPositionId == p.Id).Select(r => r.MaterialId))
                .ToHashSet());

        var plan = new Dictionary<int, PalletPosition>();
        foreach (var putaway in pendingPutaways.OrderBy(p => p.Id))
        {
            var targetInfo = targetCodes[putaway.Id];
            var target = positionsByCode[targetInfo.Code];
            var material = putaway.InwardTransactionLine.Material;

            if (!target.IsActive)
            {
                throw new InvalidOperationException($"Row {targetInfo.RowNumber}: location {target.LocationCode} is inactive.");
            }

            if (target.LocationSubtypeId != putaway.PalletPosition.LocationSubtypeId)
            {
                throw new InvalidOperationException(
                    $"Row {targetInfo.RowNumber}: location {target.LocationCode} has a different location subtype.");
            }

            if (material.RequirePreferredZone && material.PreferredZoneTypeId.HasValue
                && target.ZoneTypeId != material.PreferredZoneTypeId.Value)
            {
                throw new InvalidOperationException(
                    $"Row {targetInfo.RowNumber}: location {target.LocationCode} is outside SKU {material.MaterialNumber}'s required zone.");
            }

            if (materialIds[target.Id].Count > 0 && !materialIds[target.Id].SetEquals([material.Id]))
            {
                throw new InvalidOperationException(
                    $"Row {targetInfo.RowNumber}: location {target.LocationCode} already holds a different SKU.");
            }

            var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(target, material);
            var free = capacity - occupied[target.Id];
            if (free < putaway.QuantityBoxes)
            {
                throw new InvalidOperationException(
                    $"Row {targetInfo.RowNumber}: location {target.LocationCode} has only {free:0.###} free box(es); {putaway.QuantityBoxes:0.###} are required.");
            }

            plan[putaway.Id] = target;
            occupied[target.Id] += putaway.QuantityBoxes;
            materialIds[target.Id].Add(material.Id);
        }

        var changed = pendingPutaways.Where(p => p.PalletPositionId != plan[p.Id].Id).ToList();
        var touchedPositionIds = new HashSet<int>();
        foreach (var putaway in changed)
        {
            touchedPositionIds.Add(putaway.PalletPositionId);
            touchedPositionIds.Add(plan[putaway.Id].Id);
            putaway.PalletPositionId = plan[putaway.Id].Id;
            putaway.AllocationReason = "Location reassigned from GRN Excel upload";
            putaway.OverrideReason = request.Reason.Trim();
            putaway.RowVersion++;
        }

        if (changed.Count > 0)
        {
            foreach (var position in await db.PalletPositions
                         .Where(p => touchedPositionIds.Contains(p.Id))
                         .ToListAsync(cancellationToken))
            {
                position.RowVersion++;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "Warehouse availability changed while the workbook was being applied. No location changes were saved; download the GRN again and retry.");
            }
        }

        return new ImportInwardPutawayLocationsResult(changed.Count);
    }
}
