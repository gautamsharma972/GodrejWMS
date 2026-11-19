using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>A supervisor records boxes of a put-away that are damaged, short or do not match the inbound. The put-away row shrinks by that many
/// boxes (so the put-away plus the records can never exceed the inbound quantity), the boxes leave Good stock, and damaged boxes stay in the same
/// cell as Damage stock. Only while the GRN is not confirmed.</summary>
[RequiresPermission(PermissionKeys.Inward.Confirm)]
public sealed record RecordInwardVarianceCommand(
    int PutawayId, InwardVarianceType Type, decimal QuantityBoxes, string Remarks,
    int? ActualMaterialId = null, string? ActualMfgMonthText = null, int? DestinationPalletPositionId = null) : IRequest<InwardVarianceDto>;

public sealed class RecordInwardVarianceValidator : AbstractValidator<RecordInwardVarianceCommand>
{
    public RecordInwardVarianceValidator()
    {
        RuleFor(x => x.PutawayId).GreaterThan(0);
        RuleFor(x => x.Type).Must(t => t is InwardVarianceType.Damage or InwardVarianceType.Shortage or InwardVarianceType.Mismatch)
            .WithMessage("Choose Damage, Shortage or Mismatch. Use Add excess material for extra boxes.");
        RuleFor(x => x.QuantityBoxes).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.Remarks).NotEmpty().WithMessage("Enter a remark.").MaximumLength(500);
        When(x => x.Type == InwardVarianceType.Mismatch, () =>
        {
            RuleFor(x => x.ActualMaterialId).NotNull().GreaterThan(0).WithMessage("Select the material code that actually arrived.");
            RuleFor(x => x.ActualMfgMonthText).Must(t => t is not null && MfgMonthParser.TryParse(t, out _)).WithMessage("Select the PKM that actually arrived.");
        });
    }
}

public sealed class RecordInwardVarianceHandler(
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<RecordInwardVarianceCommand, InwardVarianceDto>
{
    public Task<InwardVarianceDto> Handle(RecordInwardVarianceCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<InwardVarianceDto> RunAsync(RecordInwardVarianceCommand request, CancellationToken cancellationToken)
    {
        var putaway = await db.InwardPutaways
            .Include(p => p.PalletPosition)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.Material)
            .Include(p => p.InwardTransactionLine).ThenInclude(l => l.InwardTransaction).ThenInclude(t => t.Lines).ThenInclude(l => l.Putaways)
            .FirstOrDefaultAsync(p => p.Id == request.PutawayId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardPutaway), request.PutawayId);
        await db.Entry(putaway).ReloadAsync(cancellationToken);

        var line = putaway.InwardTransactionLine;
        var transaction = line.InwardTransaction;
        InwardVarianceRules.EnsureOpenForReview(transaction);
        if (putaway.ExcludedLocation is not null)
        {
            throw new InvalidOperationException($"This put-away is {putaway.ExcludedLocation}; it holds no shelf stock to record against.");
        }

        if (line.MfgMonth is not { } mfgMonth)
        {
            throw new InvalidOperationException("Assign the PKM before recording damage, shortage or mismatch.");
        }

        if (request.QuantityBoxes > putaway.QuantityBoxes)
        {
            throw new InvalidOperationException(
                $"Only {putaway.QuantityBoxes:0.###} box(es) are put away at {putaway.PalletPosition.LocationCode}; {request.QuantityBoxes:0.###} cannot be recorded.");
        }

        var position = putaway.PalletPosition;

        // Damage and Mismatch boxes may be put in another location (default: they stay in the put-away's own cell).
        PalletPosition? destination = null;
        if (request.DestinationPalletPositionId is { } destinationId && destinationId != position.Id)
        {
            if (request.Type == InwardVarianceType.Shortage)
            {
                throw new InvalidOperationException("Shortage boxes did not arrive, so they have no location.");
            }

            destination = await InwardVariances.LoadDestinationAsync(db, destinationId, transaction.WarehouseId, request.Type, cancellationToken);
        }

        Material? actualMaterial = null;
        var actualMfgMonth = 0;
        if (request.Type == InwardVarianceType.Mismatch)
        {
            actualMaterial = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.ActualMaterialId, cancellationToken)
                ?? throw new InvalidOperationException("The material that actually arrived was not found.");
            if (!actualMaterial.IsActive)
            {
                throw new InvalidOperationException($"Material {actualMaterial.MaterialNumber} is inactive in Material Master.");
            }

            MfgMonthParser.TryParse(request.ActualMfgMonthText!, out actualMfgMonth);
            if (actualMaterial.Id == line.MaterialId && actualMfgMonth == mfgMonth)
            {
                throw new InvalidOperationException("The material and PKM that arrived are the same as the inbound, so this is not a mismatch.");
            }
        }

        var landing = destination ?? position;
        var changes = new List<StockChange> { new(line.MaterialId, mfgMonth, LocationSubtypeIds.Good, position, -request.QuantityBoxes) };
        if (request.Type == InwardVarianceType.Damage)
        {
            // The damaged boxes are Damage stock in inventory - in the same cell, or in the location the supervisor chose.
            changes.Add(new StockChange(line.MaterialId, mfgMonth, LocationSubtypeIds.Damage, landing, request.QuantityBoxes));
        }
        else if (request.Type == InwardVarianceType.Mismatch)
        {
            // What arrived instead is real Good stock of the other material / PKM.
            changes.Add(new StockChange(actualMaterial!.Id, actualMfgMonth, LocationSubtypeIds.Good, landing, request.QuantityBoxes));
        }

        await posting.ApplyAsync(
            new StockPostingContext(
                request.Type == InwardVarianceType.Damage ? StockLedgerEntryTypes.DamageReclass : StockLedgerEntryTypes.VarianceWriteOff,
                InwardVariances.LedgerReferenceType, transaction.ReferenceNumber, $"{request.Type} recorded: {request.Remarks.Trim()}"),
            changes, cancellationToken);

        // The row shrinks; an empty row is removed. Recording is NOT a submit: the put-away still has to be placed (by the picker, or by the
        // supervisor in the picker's absence) before the GRN can be confirmed, so the row stays in the picker's list.
        putaway.QuantityBoxes -= request.QuantityBoxes;
        line.AllocatedQuantityBoxes -= request.QuantityBoxes;
        putaway.RowVersion++;
        if (putaway.QuantityBoxes <= 0)
        {
            line.Putaways.Remove(putaway);
            db.InwardPutaways.Remove(putaway);
        }

        var variance = new InwardLineVariance
        {
            InwardTransactionId = transaction.Id,
            InwardTransactionLineId = line.Id,
            InwardPutawayId = putaway.QuantityBoxes <= 0 ? null : putaway.Id,
            Type = request.Type,
            MaterialId = line.MaterialId,
            MfgMonth = mfgMonth,
            PalletPositionId = position.Id,
            DestinationPalletPositionId = destination?.Id,
            ActualMaterialId = actualMaterial?.Id,
            ActualMfgMonth = actualMaterial is null ? null : actualMfgMonth,
            QuantityBoxes = request.QuantityBoxes,
            Remarks = request.Remarks.Trim(),
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId,
            CreatedByUserName = currentUser.UserName
        };
        db.InwardLineVariances.Add(variance);
        await InwardVariances.SaveAsync(db, cancellationToken);

        var accounted = await InwardVariances.AccountedAsync(db, transaction.Id, cancellationToken);
        InwardVariances.RefreshLineStatus(line, accounted.GetValueOrDefault((line.MaterialId, mfgMonth)));
        await InwardVariances.SaveAsync(db, cancellationToken);
        return new InwardVarianceDto(
            variance.Id, variance.Type, line.Material.MaterialNumber, line.Material.Description, MfgMonthParser.Format(mfgMonth),
            landing.LocationCode, variance.QuantityBoxes, variance.Remarks, variance.CreatedByUserName, variance.CreatedAt,
            actualMaterial?.MaterialNumber, actualMaterial is null ? null : MfgMonthParser.Format(actualMfgMonth), destination is null ? null : position.LocationCode);
    }
}

/// <summary>Removes a record again (a mistake) while the GRN is not confirmed: the stock effect is reversed and the put-away row gets its boxes back.</summary>
[RequiresPermission(PermissionKeys.Inward.Confirm)]
public sealed record RemoveInwardVarianceCommand(int VarianceId) : IRequest<Unit>;

public sealed class RemoveInwardVarianceHandler(
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<RemoveInwardVarianceCommand, Unit>
{
    public Task<Unit> Handle(RemoveInwardVarianceCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(async ct => { await RunAsync(request, ct); return Unit.Value; }, cancellationToken);

    private async Task RunAsync(RemoveInwardVarianceCommand request, CancellationToken cancellationToken)
    {
        var variance = await db.InwardLineVariances
            .Include(v => v.PalletPosition)
            .Include(v => v.DestinationPalletPosition)
            .Include(v => v.InwardTransaction).ThenInclude(t => t.Lines).ThenInclude(l => l.Putaways)
            .FirstOrDefaultAsync(v => v.Id == request.VarianceId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardLineVariance), request.VarianceId);
        var transaction = variance.InwardTransaction;
        InwardVarianceRules.EnsureOpenForReview(transaction);

        var changes = new List<StockChange>();
        switch (variance.Type)
        {
            case InwardVarianceType.Excess:
                changes.Add(new StockChange(variance.MaterialId, variance.MfgMonth, LocationSubtypeIds.Good, variance.PalletPosition, -variance.QuantityBoxes));
                break;
            case InwardVarianceType.Damage:
                changes.Add(new StockChange(variance.MaterialId, variance.MfgMonth, LocationSubtypeIds.Damage, variance.DestinationPalletPosition ?? variance.PalletPosition, -variance.QuantityBoxes));
                changes.Add(new StockChange(variance.MaterialId, variance.MfgMonth, LocationSubtypeIds.Good, variance.PalletPosition, variance.QuantityBoxes));
                break;
            case InwardVarianceType.Mismatch when variance.ActualMaterialId is { } actualId && variance.ActualMfgMonth is { } actualMonth:
                changes.Add(new StockChange(actualId, actualMonth, LocationSubtypeIds.Good, variance.DestinationPalletPosition ?? variance.PalletPosition, -variance.QuantityBoxes));
                changes.Add(new StockChange(variance.MaterialId, variance.MfgMonth, LocationSubtypeIds.Good, variance.PalletPosition, variance.QuantityBoxes));
                break;
            default:
                changes.Add(new StockChange(variance.MaterialId, variance.MfgMonth, LocationSubtypeIds.Good, variance.PalletPosition, variance.QuantityBoxes));
                break;
        }

        await posting.ApplyAsync(
            new StockPostingContext(StockLedgerEntryTypes.Reversal, InwardVariances.LedgerReferenceType, transaction.ReferenceNumber, $"{variance.Type} record removed"),
            changes, cancellationToken);

        if (variance.Type != InwardVarianceType.Excess)
        {
            var line = transaction.Lines.First(l => l.Id == variance.InwardTransactionLineId);
            var row = line.Putaways.FirstOrDefault(p => p.Id == variance.InwardPutawayId)
                ?? line.Putaways.FirstOrDefault(p => p.PalletPositionId == variance.PalletPositionId && p.ExcludedLocation == null);
            if (row is not null)
            {
                row.QuantityBoxes += variance.QuantityBoxes;
                row.RowVersion++;
            }
            else
            {
                line.Putaways.Add(new InwardPutaway
                {
                    PalletPositionId = variance.PalletPositionId,
                    QuantityBoxes = variance.QuantityBoxes,
                    AllocationReason = "Restored when a record was removed",
                    IsConfirmed = true,
                    ConfirmedAt = clock.UtcNow,
                    ConfirmedByUserId = currentUser.UserId,
                    ConfirmedByUserName = currentUser.UserName
                });
            }

            line.AllocatedQuantityBoxes += variance.QuantityBoxes;
        }

        db.InwardLineVariances.Remove(variance);
        await InwardVariances.SaveAsync(db, cancellationToken);

        if (variance.Type != InwardVarianceType.Excess)
        {
            var line = transaction.Lines.First(l => l.Id == variance.InwardTransactionLineId);
            var accounted = await InwardVariances.AccountedAsync(db, transaction.Id, cancellationToken);
            InwardVariances.RefreshLineStatus(line, accounted.GetValueOrDefault((line.MaterialId, variance.MfgMonth)));
            await InwardVariances.SaveAsync(db, cancellationToken);
        }
    }
}

/// <summary>Extra material found while unloading, beyond the inbound volume - any material, PKM and location. It is posted to stock at the chosen
/// location straight away and is not limited by the inbound quantity. Like a picker's put-away at an occupied location, a location that already holds
/// another material or would be over its pallet size returns a warning first; send the command again with <see cref="AcknowledgeWarning"/> to save it anyway.</summary>
[RequiresPermission(PermissionKeys.Inward.Confirm)]
public sealed record RecordExcessMaterialCommand(
    int TransactionId, int MaterialId, string MfgMonthText, int PalletPositionId, decimal QuantityBoxes, string Remarks, bool AcknowledgeWarning = false)
    : IRequest<RecordExcessMaterialResult>;

public sealed record RecordExcessMaterialResult(bool Saved, string? Warning, InwardVarianceDto? Record);

public sealed class RecordExcessMaterialValidator : AbstractValidator<RecordExcessMaterialCommand>
{
    public RecordExcessMaterialValidator()
    {
        RuleFor(x => x.TransactionId).GreaterThan(0);
        RuleFor(x => x.MaterialId).GreaterThan(0).WithMessage("Select the material.");
        RuleFor(x => x.PalletPositionId).GreaterThan(0).WithMessage("Select the location.");
        RuleFor(x => x.QuantityBoxes).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        RuleFor(x => x.MfgMonthText).Must(t => MfgMonthParser.TryParse(t, out _)).WithMessage("PKM must look like 'MAR|2026'.");
        RuleFor(x => x.Remarks).NotEmpty().WithMessage("Enter a remark.").MaximumLength(500);
    }
}

public sealed class RecordExcessMaterialHandler(
    IApplicationDbContext db, IStockPostingService posting, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<RecordExcessMaterialCommand, RecordExcessMaterialResult>
{
    public Task<RecordExcessMaterialResult> Handle(RecordExcessMaterialCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(ct => RunAsync(request, ct), cancellationToken);

    private async Task<RecordExcessMaterialResult> RunAsync(RecordExcessMaterialCommand request, CancellationToken cancellationToken)
    {
        var transaction = await db.InwardTransactions
            .Include(t => t.Lines).ThenInclude(l => l.Putaways)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
            ?? throw new NotFoundException(nameof(InwardTransaction), request.TransactionId);
        InwardVarianceRules.EnsureOpenForReview(transaction);

        var material = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MaterialId, cancellationToken)
            ?? throw new NotFoundException(nameof(Material), request.MaterialId);
        if (!material.IsActive)
        {
            throw new InvalidOperationException($"Material {material.MaterialNumber} is inactive in Material Master.");
        }

        var position = await db.PalletPositions.Include(p => p.Rack)
            .FirstOrDefaultAsync(p => p.Id == request.PalletPositionId, cancellationToken)
            ?? throw new InvalidOperationException("The selected location was not found.");
        if (!position.IsActive || !position.Rack.IsActive)
        {
            throw new InvalidOperationException($"Location {position.LocationCode} is not active.");
        }

        if (position.Rack.WarehouseId != transaction.WarehouseId)
        {
            throw new InvalidOperationException($"Location {position.LocationCode} belongs to a different warehouse.");
        }

        if (position.LocationSubtypeId != LocationSubtypeIds.Good || position.ZoneTypeId == ZoneTypeIds.Git)
        {
            throw new InvalidOperationException($"Location {position.LocationCode} cannot hold good stock.");
        }

        MfgMonthParser.TryParse(request.MfgMonthText, out var mfgMonth);

        if (!request.AcknowledgeWarning)
        {
            var warning = await BuildWarningAsync(position, material, mfgMonth, request.QuantityBoxes, cancellationToken);
            if (warning is not null)
            {
                return new RecordExcessMaterialResult(false, warning, null);
            }
        }

        await posting.ApplyAsync(
            new StockPostingContext(StockLedgerEntryTypes.ExcessReceived, InwardVariances.LedgerReferenceType, transaction.ReferenceNumber,
                $"Excess received: {request.Remarks.Trim()}"),
            [new StockChange(material.Id, mfgMonth, LocationSubtypeIds.Good, position, request.QuantityBoxes)], cancellationToken);

        var line = transaction.Lines.FirstOrDefault(l => l.MaterialId == material.Id && l.MfgMonth == mfgMonth);
        var variance = new InwardLineVariance
        {
            InwardTransactionId = transaction.Id,
            InwardTransactionLineId = line?.Id,
            Type = InwardVarianceType.Excess,
            MaterialId = material.Id,
            MfgMonth = mfgMonth,
            PalletPositionId = position.Id,
            QuantityBoxes = request.QuantityBoxes,
            Remarks = request.Remarks.Trim(),
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId,
            CreatedByUserName = currentUser.UserName
        };
        db.InwardLineVariances.Add(variance);
        position.RowVersion++;
        await InwardVariances.SaveAsync(db, cancellationToken);
        return new RecordExcessMaterialResult(true, null, new InwardVarianceDto(
            variance.Id, variance.Type, material.MaterialNumber, material.Description, MfgMonthParser.Format(mfgMonth),
            position.LocationCode, variance.QuantityBoxes, variance.Remarks, variance.CreatedByUserName, variance.CreatedAt));
    }

    private async Task<string?> BuildWarningAsync(PalletPosition position, Material material, int mfgMonth, decimal quantity, CancellationToken cancellationToken)
    {
        var stock = await db.StockBatches.AsNoTracking()
            .Where(b => b.PalletPositionId == position.Id && b.QuantityBoxes > 0)
            .Select(b => new { b.MaterialId, b.Material.MaterialNumber, b.QuantityBoxes })
            .ToListAsync(cancellationToken);
        var others = stock.Where(s => s.MaterialId != material.Id).Select(s => s.MaterialNumber).Distinct().ToList();
        if (others.Count > 0)
        {
            return $"{position.LocationCode} already holds material {string.Join(", ", others)}. Save the excess here anyway?";
        }

        var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position, material);
        var occupied = stock.Sum(s => s.QuantityBoxes);
        if (occupied + quantity > capacity)
        {
            return $"{position.LocationCode} holds {occupied:0.###} box(es) and fits {capacity:0.###}; adding {quantity:0.###} goes over its pallet size. Save the excess here anyway?";
        }

        return null;
    }
}

internal static class InwardVarianceRules
{
    /// <summary>Records can be added or removed only on a posted, non-rejected GRN whose put-away the supervisor has not confirmed (locked) yet.</summary>
    public static void EnsureOpenForReview(InwardTransaction transaction)
    {
        if (transaction.IsRejected)
        {
            throw new InvalidOperationException("This GRN has been rejected.");
        }

        if (transaction.PostedAt is null)
        {
            throw new InvalidOperationException("This GRN has not been posted to inventory, so damage, shortage, mismatch and excess cannot be recorded against it.");
        }

        // PICK-RESERVED rows are closed (locked) by a pullout, not by a confirmation: only the other rows say whether the GRN is confirmed.
        if (transaction.Lines.SelectMany(l => l.Putaways).Any(p => p.IsExcludedLocked && p.ExcludedLocation != PutawayPickReservation.Label))
        {
            throw new InvalidOperationException("This GRN has already been confirmed, so records can no longer be added or removed.");
        }
    }
}
