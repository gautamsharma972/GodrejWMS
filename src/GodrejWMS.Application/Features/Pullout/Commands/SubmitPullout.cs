using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Saves a pending preview, refreshes an existing draft, or confirms reviewed picks.
/// <paramref name="HeaderFields"/> and <paramref name="WarehouseId"/> are only applied on the very
/// first call for a new pullout (when both <paramref name="ConfirmedPreview"/> and
/// <paramref name="RefreshReference"/> are null) - they're persisted on the transaction then, and
/// every later confirm/refresh call reads them back from there, so they don't need to be (and
/// aren't) re-sent.</summary>
public sealed record SubmitPulloutCommand(IReadOnlyList<SubmitPulloutLine> Lines,
    PulloutResultDto? ConfirmedPreview = null, string? RefreshReference = null,
    PulloutLoadingSheetFieldsDto? HeaderFields = null, int? WarehouseId = null) : IRequest<PulloutResultDto>;

public sealed record SubmitPulloutLine(long MaterialCode, decimal QuantityBoxes, string? PlantName = null, string? Priority = null, string? MaterialDescription = null);

public sealed class SubmitPulloutValidator : AbstractValidator<SubmitPulloutCommand>
{
    public SubmitPulloutValidator(IWarehouseAccessService warehouseAccess)
    {
        RuleFor(x => x.WarehouseId)
            .NotNull()
            .When(x => x.ConfirmedPreview is null && x.RefreshReference is null)
            .WithMessage("Select a warehouse.");
        RuleFor(x => x.WarehouseId!.Value)
            .MustAsync((id, ct) => warehouseAccess.HasAccessAsync(id, ct))
            .When(x => x.WarehouseId is not null)
            .WithMessage("Warehouse is not assigned to you.");
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialCode).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).Must(decimal.IsInteger).WithMessage("Quantity must be a whole number (no decimals).");
        });
    }
}

public sealed class SubmitPulloutHandler(
    IApplicationDbContext db,
    IPulloutAllocationService allocator,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<SubmitPulloutCommand, PulloutResultDto>
{
    public async Task<PulloutResultDto> Handle(SubmitPulloutCommand request, CancellationToken cancellationToken)
    {
        var reference = request.ConfirmedPreview?.ReferenceNumber ?? request.RefreshReference;
        PulloutTransaction transaction;
        PulloutResultDto? savedPreview = null;
        var lines = request.Lines;
        if (reference is not null)
        {
            transaction = await db.PulloutTransactions.Include(t => t.Lines).ThenInclude(l => l.Picks)
                .SingleOrDefaultAsync(t => t.ReferenceNumber == reference, cancellationToken)
                ?? throw new InvalidOperationException("Saved pullout was not found.");
            // Reload the header so a long-lived Blazor scope sees confirmations from other sessions.
            await db.Entry(transaction).ReloadAsync(cancellationToken);
            if (transaction.IsRejected)
                throw new InvalidOperationException("This pullout was rejected and can no longer be refreshed or confirmed.");
            if (transaction.IsConfirmed)
                throw new InvalidOperationException("This pullout has already been confirmed.");
            savedPreview = JsonSerializer.Deserialize<PulloutResultDto>(transaction.PreviewJson!)!;
            lines = savedPreview.Lines.Select(l => new SubmitPulloutLine(l.MaterialNumber, l.RequestedQuantityBoxes, l.PlantName, l.Priority, l.MaterialDescription)).ToList();
        }
        else
        {
            var createdAt = clock.UtcNow;
            transaction = new PulloutTransaction
            {
                WarehouseId = request.WarehouseId!.Value,
                ReferenceNumber = await NextReferenceAsync(createdAt, cancellationToken), CreatedAt = createdAt,
                CreatedByUserId = currentUser.UserId, IsConfirmed = false
            };
            if (request.HeaderFields is { } fields)
            {
                transaction.TransportName = fields.TransportName;
                transaction.VehicleNumber = fields.VehicleNumber;
                transaction.GateNumber = fields.GateNumber;
                transaction.DriverNumber = fields.DriverNumber;
                transaction.PlanTime = fields.PlanTime;
                transaction.DateText = fields.DateText;
                transaction.StartTime = fields.StartTime;
                transaction.EndTime = fields.EndTime;
                transaction.WeightText = fields.WeightText;
                transaction.VolumeText = fields.VolumeText;
            }
        }

        var materials = await db.Materials.AsNoTracking()
            .Where(m => lines.Select(l => l.MaterialCode).Contains(m.MaterialNumber))
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);
        var resultLines = new List<PulloutLineResultDto>();
        var proposedLines = new List<PulloutTransactionLine>();
        var plantLines = lines
            .GroupBy(l => new PlantLineKey(l.MaterialCode, NormalizePlantKey(l.PlantName)))
            .Select(g => new SubmitPulloutLine(
                g.Key.MaterialCode,
                g.Sum(l => l.QuantityBoxes),
                g.Select(l => l.PlantName?.Trim()).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                g.Select(l => l.Priority?.Trim()).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                g.Select(l => l.MaterialDescription?.Trim()).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))))
            .ToList();

        foreach (var materialLines in plantLines.GroupBy(l => l.MaterialCode))
        {
            if (!materials.TryGetValue(materialLines.Key, out var material))
            {
                foreach (var line in materialLines)
                {
                    // Material isn't in Material Master yet (see error list on import) - show the
                    // uploaded sheet's own Description for this row instead of a generic
                    // placeholder, so the output stays readable and matches what was actually
                    // requested. Falls back to the placeholder only for manually-entered lines,
                    // which have no description to carry through.
                    var description = string.IsNullOrWhiteSpace(line.MaterialDescription) ? "(unknown material)" : line.MaterialDescription;
                    resultLines.Add(new(line.MaterialCode, description, line.QuantityBoxes, 0,
                        Domain.Enums.AllocationStatus.Failed, "Material code not found in Material Master.", [], line.PlantName, line.Priority));
                }
                continue;
            }

            // Allocate the SKU once, then distribute its FIFO picks across the destination-plant
            // lines in input order. Calling the preview allocator once per plant would allow the
            // same stock batch to be proposed more than once.
            var allocation = await allocator.AllocateAsync(
                material.Id, materialLines.Sum(l => l.QuantityBoxes), transaction.WarehouseId, cancellationToken,
                preview: true, excludePulloutTransactionId: reference is null ? null : transaction.Id);
            var availablePicks = new Queue<RemainingPick>(allocation.Picks.Select(p => new RemainingPick(
                p.LocationCode, p.MfgMonth, p.QuantityBoxes)));

            foreach (var line in materialLines)
            {
                var linePicks = TakePicks(availablePicks, line.QuantityBoxes);
                var pickedQuantity = linePicks.Sum(p => p.QuantityBoxes);
                var status = pickedQuantity == line.QuantityBoxes
                    ? Domain.Enums.AllocationStatus.Fulfilled
                    : pickedQuantity > 0
                        ? Domain.Enums.AllocationStatus.Partial
                        : Domain.Enums.AllocationStatus.Failed;
                var remarks = status == Domain.Enums.AllocationStatus.Fulfilled
                    ? null
                    : $"Insufficient good stock - {line.QuantityBoxes - pickedQuantity:0.###} box(es) short across all pickable locations.";
                var transactionLine = new PulloutTransactionLine
                {
                    MaterialId = material.Id, RequestedQuantityBoxes = line.QuantityBoxes,
                    PickedQuantityBoxes = pickedQuantity, Status = status, Remarks = remarks,
                    PlantName = line.PlantName, Priority = line.Priority
                };
                foreach (var pick in linePicks)
                {
                    var position = await db.PalletPositions.AsNoTracking()
                        .FirstAsync(p => p.LocationCode == pick.LocationCode, cancellationToken);
                    transactionLine.Picks.Add(new PulloutPick
                    {
                        PalletPositionId = position.Id, MfgMonth = pick.MfgMonth, QuantityBoxes = pick.QuantityBoxes
                    });
                }
                proposedLines.Add(transactionLine);
                resultLines.Add(new(material.MaterialNumber, material.Description, line.QuantityBoxes,
                    pickedQuantity, status, remarks,
                    linePicks.Select(p => new PulloutPickDto(p.LocationCode, MfgMonthParser.Format(p.MfgMonth), p.QuantityBoxes)).ToList(),
                    line.PlantName, line.Priority));
            }
        }
        var headerFields = new PulloutLoadingSheetFieldsDto(
            transaction.TransportName, transaction.VehicleNumber, transaction.GateNumber, transaction.DriverNumber,
            transaction.PlanTime, transaction.DateText, transaction.StartTime, transaction.EndTime,
            transaction.WeightText, transaction.VolumeText);
        var warehouseAddress = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == transaction.WarehouseId)
            .Select(w => w.Address)
            .FirstOrDefaultAsync(cancellationToken);
        var result = new PulloutResultDto(transaction.ReferenceNumber, transaction.CreatedAt, resultLines, headerFields, warehouseAddress);
        if (request.ConfirmedPreview is not null)
        {
            if (!SamePicks(savedPreview!, request.ConfirmedPreview))
                throw new InvalidOperationException("The saved picks have changed. Reload the pullout, review them, then confirm again.");
            if (!savedPreview!.Lines.Any(l => l.PickedQuantityBoxes > 0))
                throw new InvalidOperationException("No pickable stock is available. Refresh the picks when stock is available.");

            result = savedPreview;
        }

        try
        {
            if (request.ConfirmedPreview is not null)
            {
                // The saved allocation may contain an audited operator location override. Apply the
                // reviewed picks exactly as saved instead of silently recomputing FIFO locations.
                foreach (var entry in db.ChangeTracker.Entries<StockBatch>().ToList())
                    entry.State = EntityState.Detached;
                await ApplySavedPicksAsync(result, materials, cancellationToken);
                transaction.IsConfirmed = true;
                transaction.ConfirmedAt = clock.UtcNow;
                transaction.ConfirmedByUserId = currentUser.UserId;
                transaction.ConfirmedByUserName = currentUser.UserName;
            }
            else
            {
                db.PulloutTransactionLines.RemoveRange(transaction.Lines);
                transaction.Lines = proposedLines;
                transaction.PreviewJson = JsonSerializer.Serialize(result);
                if (reference is null) db.PulloutTransactions.Add(transaction);
                await TouchReservedStockAsync(proposedLines, cancellationToken);
            }
            transaction.RowVersion++;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new InvalidOperationException(
                        "Stock was reserved by another pullout while this order was being uploaded. Upload or refresh the order again to allocate the remaining available stock.");
                }
                catch (DbUpdateException) when (reference is null && attempt < 4)
                {
                    // The unique reference index arbitrates simultaneous requests for the same sequence.
                    if (!await db.PulloutTransactions.AsNoTracking().AnyAsync(
                        t => t.ReferenceNumber == transaction.ReferenceNumber, cancellationToken))
                        throw;
                    foreach (var entry in db.ChangeTracker.Entries<ActivityLog>()
                        .Where(e => e.State == EntityState.Added).ToList())
                        entry.State = EntityState.Detached;
                    transaction.ReferenceNumber = await NextReferenceAsync(transaction.CreatedAt, cancellationToken);
                    result = result with { ReferenceNumber = transaction.ReferenceNumber };
                    transaction.PreviewJson = JsonSerializer.Serialize(result);
                }
            }
            return result;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task TouchReservedStockAsync(
        IReadOnlyList<PulloutTransactionLine> lines,
        CancellationToken cancellationToken)
    {
        var materialIds = lines.Select(line => line.MaterialId).Distinct().ToArray();
        var positionIds = lines.SelectMany(line => line.Picks).Select(pick => pick.PalletPositionId).Distinct().ToArray();
        var mfgMonths = lines.SelectMany(line => line.Picks).Select(pick => pick.MfgMonth).Distinct().ToArray();
        var batches = await db.StockBatches
            .Where(batch => materialIds.Contains(batch.MaterialId)
                && positionIds.Contains(batch.PalletPositionId)
                && mfgMonths.Contains(batch.MfgMonth))
            .ToListAsync(cancellationToken);
        foreach (var batch in batches)
            batch.RowVersion++;
    }

    private async Task ApplySavedPicksAsync(
        PulloutResultDto preview,
        IReadOnlyDictionary<long, Material> materials,
        CancellationToken cancellationToken)
    {
        foreach (var line in preview.Lines.Where(l => materials.ContainsKey(l.MaterialNumber)))
        {
            foreach (var pick in line.Picks)
            {
                if (!MfgMonthParser.TryParse(pick.MfgMonthLabel, out var mfgMonth))
                    throw new InvalidOperationException($"Pick at {pick.LocationCode} has an invalid PKM period.");

                var batch = await db.StockBatches
                    .Include(b => b.PalletPosition)
                    .SingleOrDefaultAsync(b => b.MaterialId == materials[line.MaterialNumber].Id
                        && b.MfgMonth == mfgMonth
                        && b.StockSubtypeId == LocationSubtypeIds.Good
                        && b.PalletPosition.LocationCode == pick.LocationCode,
                        cancellationToken);

                if (batch is null
                    || !batch.PalletPosition.IsActive
                    || batch.PalletPosition.LocationSubtypeId != LocationSubtypeIds.Good
                    || batch.QuantityBoxes < pick.QuantityBoxes)
                {
                    throw new InvalidOperationException(
                        $"Stock at {pick.LocationCode} for {pick.MfgMonthLabel} is no longer available in the allocated quantity. Reload and review the pullout before confirming.");
                }

                batch.QuantityBoxes -= pick.QuantityBoxes;
                batch.RowVersion++;
                if (batch.QuantityBoxes <= 0)
                    db.StockBatches.Remove(batch);
            }
        }
    }

    private async Task<string> NextReferenceAsync(DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var prefix = $"PICK-{createdAt:yyyyMMdd}-";
        var references = await db.PulloutTransactions.AsNoTracking()
            .Where(t => t.ReferenceNumber.StartsWith(prefix))
            .Select(t => t.ReferenceNumber).ToListAsync(cancellationToken);
        var sequence = references.Select(r => int.TryParse(r[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0).Max() + 1;
        if (sequence > 999999)
            throw new InvalidOperationException("The daily pullout reference sequence is exhausted.");
        return $"{prefix}{sequence:D6}";
    }

    private static string NormalizePlantKey(string? plantName) =>
        string.IsNullOrWhiteSpace(plantName) ? string.Empty : plantName.Trim().ToUpperInvariant();

    private static List<RemainingPick> TakePicks(Queue<RemainingPick> availablePicks, decimal requestedQuantity)
    {
        var result = new List<RemainingPick>();
        var remaining = requestedQuantity;
        while (remaining > 0 && availablePicks.TryPeek(out var available))
        {
            var quantity = Math.Min(remaining, available.QuantityBoxes);
            result.Add(available with { QuantityBoxes = quantity });
            remaining -= quantity;
            available.QuantityBoxes -= quantity;
            if (available.QuantityBoxes <= 0)
                availablePicks.Dequeue();
        }
        return result;
    }

    private sealed record PlantLineKey(long MaterialCode, string PlantNameKey);

    private sealed record RemainingPick(string LocationCode, int MfgMonth, decimal InitialQuantityBoxes)
    {
        public decimal QuantityBoxes { get; set; } = InitialQuantityBoxes;
    }

    private static bool SamePicks(PulloutResultDto left, PulloutResultDto right)
    {
        static string Normalize(PulloutResultDto result) => JsonSerializer.Serialize(result.Lines
            .OrderBy(l => l.MaterialNumber).Select(l => new
            {
                l.MaterialNumber, l.RequestedQuantityBoxes, l.PickedQuantityBoxes,
                Picks = l.Picks.OrderBy(p => p.LocationCode).ThenBy(p => p.MfgMonthLabel)
                    .Select(p => new { p.LocationCode, p.MfgMonthLabel, p.QuantityBoxes })
            }));
        return Normalize(left) == Normalize(right);
    }
}
