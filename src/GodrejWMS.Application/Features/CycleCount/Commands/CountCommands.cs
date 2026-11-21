using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Commands;

/// <summary>Opens a batch for counting: takes the system-stock snapshot (once) and marks the batch In Progress.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Perform, PermissionKeys.CycleCount.ManageSchedule)]
public sealed record StartCycleCountCommand(int BatchId) : IRequest<CycleCountBatchDto>;

[RequiresPermission(PermissionKeys.CycleCount.Perform)]
public sealed record VerifyCycleCountCardCommand(int CardId) : IRequest<CycleCountCardDto>;

/// <summary>Saves the physical count of a card. Material and PKM may be changed; quantity may be zero.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Perform)]
public sealed record CorrectCycleCountCardCommand(int CardId, long? MaterialNumber, string? MfgMonthText, decimal PhysicalQuantityBoxes, int? StockSubtypeId = null) : IRequest<CycleCountCardDto>;

/// <summary>Stock found at a location in addition to what the system lists there.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Perform)]
public sealed record AddCycleCountFoundStockCommand(int BatchId, int PalletPositionId, long MaterialNumber, string MfgMonthText, decimal PhysicalQuantityBoxes, int StockSubtypeId = LocationSubtypeIds.Good) : IRequest<CycleCountCardDto>;

/// <summary>The counters also want to count the damaged stock of a rack (by default only Good stock is counted). Adds the damage cards once.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Perform)]
public sealed record EnableCycleCountDamageCountingCommand(int BatchId, string RackCode) : IRequest<int>;

public sealed class CorrectCycleCountCardValidator : AbstractValidator<CorrectCycleCountCardCommand>
{
    public CorrectCycleCountCardValidator()
    {
        RuleFor(x => x.StockSubtypeId).Must(i => i is null or LocationSubtypeIds.Good or LocationSubtypeIds.Damage).WithMessage("Stock type must be Good or Damage.");
        RuleFor(x => x.PhysicalQuantityBoxes).GreaterThanOrEqualTo(0).WithMessage("Quantity cannot be negative.")
            .Must(decimal.IsInteger).WithMessage("Quantity must be a whole number (no decimals).");
    }
}

public sealed class AddCycleCountFoundStockValidator : AbstractValidator<AddCycleCountFoundStockCommand>
{
    public AddCycleCountFoundStockValidator()
    {
        RuleFor(x => x.StockSubtypeId).Must(i => i is LocationSubtypeIds.Good or LocationSubtypeIds.Damage).WithMessage("Stock type must be Good or Damage.");
        RuleFor(x => x.MaterialNumber).GreaterThan(0).WithMessage("Enter the material code.");
        RuleFor(x => x.PhysicalQuantityBoxes).GreaterThan(0).WithMessage("Quantity must be greater than zero.")
            .Must(decimal.IsInteger).WithMessage("Quantity must be a whole number (no decimals).");
    }
}

public sealed class StartCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<StartCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(StartCycleCountCommand request, CancellationToken cancellationToken)
    {
        var started = false;
        CycleCountBatch batch;
        try
        {
            batch = await uow.ExecuteInTransactionAsync(async ct =>
            {
                var b = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, ct);
                if (b.CardsGenerated && CycleCountCounting.CountingStatuses.Contains(b.Status))
                {
                    // Already started. A batch started before damaged stock was offered by default gets its damage cards now.
                    var missing = b.Racks.Where(r => r.Status == CycleCountRackStatus.Open && !r.IncludesDamageStock).ToList();
                    if (missing.Count == 0) return b;
                    var topUpNow = clock.UtcNow;
                    foreach (var rack in missing) await CycleCountCounting.GenerateDamageCardsAsync(db, b, rack, user, topUpNow, ct);
                    await db.SaveChangesAsync(ct);
                    return b;
                }
                if (b.Status is not (CycleCountBatchStatus.Scheduled or CycleCountBatchStatus.InProgress or CycleCountBatchStatus.Extended))
                {
                    CycleCountCounting.EnsureCounting(b);
                    return b;
                }

                var now = clock.UtcNow;
                if (now < b.ScheduleFrom)
                {
                    throw new InvalidOperationException($"This cycle count starts on {b.ScheduleFrom.ToLocalTime():dd MMM yyyy HH:mm}.");
                }

                if (!b.CardsGenerated) await CycleCountCounting.GenerateCardsAsync(db, b, user, now, ct);
                if (b.Status == CycleCountBatchStatus.Scheduled) b.Status = CycleCountBatchStatus.InProgress;
                b.ActualStartAt ??= now;
                CycleCountRules.Touch(b, user, now);
                CycleCountRules.AddHistory(db, b, CycleCountActions.Started, user, now, null, "Count cards generated from the current system stock");
                await db.SaveChangesAsync(ct);
                started = true;
                return b;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another counter started the batch at the same moment: use what they created.
            db.ChangeTracker.Clear();
            batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        }

        if (started)
        {
            await CycleCountNotify.PublishAsync(notifications, clock.UtcNow, "Cycle count started", $"{batch.BatchNumber} is open for counting.", url: "/cycle-count/count", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        }

        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

public sealed class VerifyCycleCountCardHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user)
    : IRequestHandler<VerifyCycleCountCardCommand, CycleCountCardDto>
{
    public async Task<CycleCountCardDto> Handle(VerifyCycleCountCardCommand request, CancellationToken cancellationToken)
    {
        var card = await CycleCountCounting.LoadCardAsync(db, access, request.CardId, cancellationToken);
        CycleCountCounting.EnsureEditable(card);
        if (card.IsExtra)
        {
            throw new InvalidOperationException("Stock that the system does not list has to be corrected, not confirmed.");
        }

        var now = clock.UtcNow;
        var numbers = await NumbersAsync(db, card, cancellationToken);
        var previous = CycleCountCounting.Describe(card, numbers);
        var wasCompleted = card.State != CycleCountCardState.Pending;
        card.PhysicalMaterialId = card.SystemMaterialId;
        card.PhysicalMfgMonth = card.SystemMfgMonth;
        card.PhysicalQuantityBoxes = card.SystemQuantityBoxes;
        card.PhysicalStockSubtypeId = card.SystemStockSubtypeId;
        card.DifferenceBoxes = 0;
        card.State = CycleCountCardState.Verified;
        card.CountedByUserId = user.UserId;
        card.CountedByUserName = user.UserName;
        card.CountedAt = now;
        card.RowVersion++;
        card.UpdatedAt = now;
        card.UpdatedByUserId = user.UserId;
        CycleCountRules.AddHistory(db, card.CycleCountBatch, wasCompleted ? CycleCountActions.Reopened : CycleCountActions.Verified, user, now,
            previous, CycleCountCounting.Describe(card, numbers), rackCode: card.RackCode, cardId: card.Id);
        await db.SaveChangesAsync(cancellationToken);
        return (await CycleCountCounting.MapAsync(db, [card], cancellationToken))[0];
    }

    internal static async Task<Dictionary<int, long>> NumbersAsync(IApplicationDbContext db, CycleCountCard card, CancellationToken cancellationToken)
    {
        var ids = new[] { card.SystemMaterialId, card.PhysicalMaterialId }.Where(i => i != null).Select(i => i!.Value).ToList();
        return await db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.MaterialNumber, cancellationToken);
    }
}

public sealed class CorrectCycleCountCardHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user)
    : IRequestHandler<CorrectCycleCountCardCommand, CycleCountCardDto>
{
    public async Task<CycleCountCardDto> Handle(CorrectCycleCountCardCommand request, CancellationToken cancellationToken)
    {
        var card = await CycleCountCounting.LoadCardAsync(db, access, request.CardId, cancellationToken);
        CycleCountCounting.EnsureEditable(card);
        var numbers = await VerifyCycleCountCardHandler.NumbersAsync(db, card, cancellationToken);
        var previous = CycleCountCounting.Describe(card, numbers);

        int? materialId = null;
        int? pkm = null;
        var subtype = request.StockSubtypeId ?? (card.IsExtra || card.SystemMaterialId is null ? LocationSubtypeIds.Good : card.SystemStockSubtypeId);
        if (request.PhysicalQuantityBoxes > 0)
        {
            // Quantity above zero needs a material and a PKM; they default to what the system lists.
            var number = request.MaterialNumber ?? (card.SystemMaterialId is { } sm ? numbers[sm] : (long?)null);
            if (number is null) throw new InvalidOperationException("Enter the material code of the stock found here.");
            var material = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.MaterialNumber == number.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Material code {number} was not found in Material Master.");
            if (!material.IsActive && material.Id != card.SystemMaterialId)
            {
                throw new InvalidOperationException($"Material {number} is inactive and cannot be recorded as new stock.");
            }

            materialId = material.Id;
            if (string.IsNullOrWhiteSpace(request.MfgMonthText))
            {
                pkm = card.SystemMfgMonth ?? throw new InvalidOperationException("Enter the PKM of the stock found here.");
            }
            else if (MfgMonthParser.TryParse(request.MfgMonthText, out var parsed))
            {
                pkm = parsed;
            }
            else
            {
                throw new InvalidOperationException("PKM must look like 'MAR|2026'.");
            }

            await EnsureNoDuplicateAsync(card, materialId.Value, pkm.Value, subtype, cancellationToken);
        }

        var now = clock.UtcNow;
        var unchanged = materialId == card.SystemMaterialId && pkm == card.SystemMfgMonth && request.PhysicalQuantityBoxes == card.SystemQuantityBoxes && subtype == card.SystemStockSubtypeId
            || (card.SystemMaterialId is null && request.PhysicalQuantityBoxes == 0);
        if (unchanged && !card.IsExtra)
        {
            // Entered figures equal the system: the same as pressing OK.
            card.PhysicalMaterialId = card.SystemMaterialId;
            card.PhysicalMfgMonth = card.SystemMfgMonth;
            card.PhysicalQuantityBoxes = card.SystemQuantityBoxes;
            card.PhysicalStockSubtypeId = card.SystemStockSubtypeId;
            card.DifferenceBoxes = 0;
            card.State = CycleCountCardState.Verified;
        }
        else
        {
            card.PhysicalMaterialId = materialId;
            card.PhysicalMfgMonth = pkm;
            card.PhysicalQuantityBoxes = request.PhysicalQuantityBoxes;
            card.PhysicalStockSubtypeId = subtype;
            card.DifferenceBoxes = request.PhysicalQuantityBoxes - card.SystemQuantityBoxes;
            card.State = CycleCountCardState.Corrected;
        }

        var wasCompleted = previous != "pending";
        card.CountedByUserId = user.UserId;
        card.CountedByUserName = user.UserName;
        card.CountedAt = now;
        card.RowVersion++;
        card.UpdatedAt = now;
        card.UpdatedByUserId = user.UserId;
        numbers = await VerifyCycleCountCardHandler.NumbersAsync(db, card, cancellationToken);
        CycleCountRules.AddHistory(db, card.CycleCountBatch, wasCompleted ? CycleCountActions.Reopened : CycleCountActions.Corrected, user, now,
            previous, CycleCountCounting.Describe(card, numbers), rackCode: card.RackCode, cardId: card.Id);
        await db.SaveChangesAsync(cancellationToken);
        return (await CycleCountCounting.MapAsync(db, [card], cancellationToken))[0];
    }

    /// <summary>Two cards of one location may not claim the same material + PKM: the stock would be counted twice.</summary>
    private async Task EnsureNoDuplicateAsync(CycleCountCard card, int materialId, int pkm, int subtype, CancellationToken cancellationToken)
    {
        var others = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == card.CycleCountBatchId && c.PalletPositionId == card.PalletPositionId && c.Id != card.Id && !c.IsExcluded)
            .ToListAsync(cancellationToken);
        var clash = others.Select(CycleCountCounting.EffectiveStock).Any(s => s is { } e && e.MaterialId == materialId && e.Pkm == pkm && e.SubtypeId == subtype);
        if (clash)
        {
            throw new InvalidOperationException("Another card of this location already records this material and PKM. Change that card instead so the stock is not counted twice.");
        }
    }
}

public sealed class AddCycleCountFoundStockHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user)
    : IRequestHandler<AddCycleCountFoundStockCommand, CycleCountCardDto>
{
    public async Task<CycleCountCardDto> Handle(AddCycleCountFoundStockCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        CycleCountCounting.EnsureCounting(batch);
        var anchor = await db.CycleCountCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CycleCountBatchId == batch.Id && c.PalletPositionId == request.PalletPositionId, cancellationToken)
            ?? throw new InvalidOperationException("This location is not part of the cycle count.");
        var rack = CycleCountCounting.FindRack(batch, anchor.RackCode);
        if (rack.Status == CycleCountRackStatus.Submitted) throw new InvalidOperationException("This rack has been submitted; its records can no longer be changed.");
        if (rack.Status == CycleCountRackStatus.Excluded) throw new InvalidOperationException("This rack was excluded from the cycle count.");

        var material = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.MaterialNumber == request.MaterialNumber, cancellationToken)
            ?? throw new InvalidOperationException($"Material code {request.MaterialNumber} was not found in Material Master.");
        if (!material.IsActive) throw new InvalidOperationException($"Material {request.MaterialNumber} is inactive and cannot be recorded as new stock.");
        if (!MfgMonthParser.TryParse(request.MfgMonthText, out var pkm)) throw new InvalidOperationException("PKM must look like 'MAR|2026'.");

        var others = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == batch.Id && c.PalletPositionId == request.PalletPositionId && !c.IsExcluded).ToListAsync(cancellationToken);
        if (others.Select(CycleCountCounting.EffectiveStock).Any(s => s is { } e && e.MaterialId == material.Id && e.Pkm == pkm && e.SubtypeId == request.StockSubtypeId))
        {
            throw new InvalidOperationException("A card of this location already records this material and PKM. Change that card instead so the stock is not counted twice.");
        }

        var position = await db.PalletPositions.AsNoTracking().Where(p => p.Id == request.PalletPositionId).Select(p => new { p.Column, p.Level }).FirstAsync(cancellationToken);
        var now = clock.UtcNow;
        var card = new CycleCountCard
        {
            CycleCountBatchId = batch.Id, RackCode = anchor.RackCode, PalletPositionId = request.PalletPositionId,
            SequenceKey = CycleCountRules.SequenceKey(anchor.RackCode, position.Column, position.Level, material.MaterialNumber, request.StockSubtypeId),
            PhysicalMaterialId = material.Id, PhysicalMfgMonth = pkm, PhysicalQuantityBoxes = request.PhysicalQuantityBoxes, PhysicalStockSubtypeId = request.StockSubtypeId,
            DifferenceBoxes = request.PhysicalQuantityBoxes, State = CycleCountCardState.Corrected, IsExtra = true,
            CountedByUserId = user.UserId, CountedByUserName = user.UserName, CountedAt = now,
            CreatedAt = now, CreatedByUserId = user.UserId, RowVersion = 1
        };
        db.CycleCountCards.Add(card);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.FoundStockAdded, user, now, null,
            $"{material.MaterialNumber} {MfgMonthParser.Format(pkm)} x {request.PhysicalQuantityBoxes:0.###}", rackCode: anchor.RackCode);
        await db.SaveChangesAsync(cancellationToken);
        return (await CycleCountCounting.MapAsync(db, [card], cancellationToken))[0];
    }
}

public sealed class EnableCycleCountDamageCountingHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUserService user)
    : IRequestHandler<EnableCycleCountDamageCountingCommand, int>
{
    public Task<int> Handle(EnableCycleCountDamageCountingCommand request, CancellationToken cancellationToken) =>
        uow.ExecuteInTransactionAsync(async ct =>
        {
            var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, ct);
            CycleCountCounting.EnsureCounting(batch);
            var rack = CycleCountCounting.FindRack(batch, request.RackCode);
            await db.Entry(rack).ReloadAsync(ct);
            if (rack.Status != CycleCountRackStatus.Open) throw new InvalidOperationException("This rack can no longer be changed.");
            if (rack.IncludesDamageStock) return 0; // already on

            var now = clock.UtcNow;
            var added = await CycleCountCounting.GenerateDamageCardsAsync(db, batch, rack, user, now, ct);
            CycleCountRules.AddHistory(db, batch, CycleCountActions.DamageCountingEnabled, user, now, null, $"{added} damage card(s) added", rackCode: rack.RackCode);
            await db.SaveChangesAsync(ct);
            return added;
        }, cancellationToken);
}
