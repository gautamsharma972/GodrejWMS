using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Commands;

[RequiresPermission(PermissionKeys.CycleCount.Schedule)]
public sealed record ScheduleCycleCountCommand(
    int WarehouseId, DateTimeOffset ScheduleFrom, DateTimeOffset ScheduleTo, IReadOnlyList<string> RackCodes, string? Remarks = null,
    bool AcknowledgeWarnings = false, bool AutoReleaseFreezeAtScheduleEnd = false) : IRequest<CycleCountBatchDto>;

[RequiresPermission(PermissionKeys.CycleCount.ManageSchedule)]
public sealed record ExtendCycleCountCommand(int BatchId, DateTimeOffset NewScheduleTo, string Reason) : IRequest<CycleCountBatchDto>;

[RequiresPermission(PermissionKeys.CycleCount.ManageSchedule)]
public sealed record ReduceCycleCountCommand(int BatchId, DateTimeOffset NewScheduleTo, string Reason, bool AcknowledgeIncomplete = false) : IRequest<CycleCountBatchDto>;

[RequiresPermission(PermissionKeys.CycleCount.ManageSchedule)]
public sealed record PartiallyCloseCycleCountCommand(int BatchId, string Reason) : IRequest<CycleCountBatchDto>;

[RequiresPermission(PermissionKeys.CycleCount.ManageSchedule)]
public sealed record DropCycleCountCommand(int BatchId, string Reason) : IRequest<CycleCountBatchDto>;

[RequiresPermission(PermissionKeys.CycleCount.ManageSchedule)]
public sealed record CloseCycleCountCommand(int BatchId, string? Remarks = null) : IRequest<CycleCountBatchDto>;

public sealed class ScheduleCycleCountValidator : AbstractValidator<ScheduleCycleCountCommand>
{
    public ScheduleCycleCountValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0).WithMessage("Select the warehouse.");
        RuleFor(x => x.RackCodes).NotEmpty().WithMessage("Select at least one rack.");
        RuleFor(x => x.ScheduleTo).GreaterThan(x => x.ScheduleFrom).WithMessage("Schedule To must be after Schedule From.");
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

public sealed class ExtendCycleCountValidator : AbstractValidator<ExtendCycleCountCommand>
{
    public ExtendCycleCountValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for extending.").MaximumLength(500);
}

public sealed class ReduceCycleCountValidator : AbstractValidator<ReduceCycleCountCommand>
{
    public ReduceCycleCountValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for reducing.").MaximumLength(500);
}

public sealed class PartiallyCloseCycleCountValidator : AbstractValidator<PartiallyCloseCycleCountCommand>
{
    public PartiallyCloseCycleCountValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Enter the reason for closing partially.").MaximumLength(500);
}

public sealed class DropCycleCountValidator : AbstractValidator<DropCycleCountCommand>
{
    public DropCycleCountValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("A reason is required to drop a cycle count.").MaximumLength(500);
}

internal static class CycleCountNotify
{
    /// <summary>Best effort: the change is already saved when this runs, so a failed delivery (for example a browser tab that just disconnected)
    /// must never turn a successful action into an error for the user.</summary>
    public static async Task PublishAsync(IRealtimeNotificationService notifications, DateTimeOffset now, string title, string message, string kind = "info", string url = "/cycle-count/batches", string? excludeUserId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await notifications.PublishAsync(new RealtimeNotification(
                Guid.NewGuid(), title, message, kind, url, now,
                AudienceRoles: ["Superadmin", "Head Supervisor", "Cycle Count Scheduler", "Cycle Count Performer"], ExcludeUserId: excludeUserId), cancellationToken);
        }
        catch (Exception)
        {
            // intentionally ignored
        }
    }
}

public sealed class ScheduleCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<ScheduleCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(ScheduleCycleCountCommand request, CancellationToken cancellationToken)
    {
        if (!await access.HasAccessAsync(request.WarehouseId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var codes = request.RackCodes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0)
        {
            throw new InvalidOperationException("Select at least one rack.");
        }

        var rackCodes = await db.Racks.AsNoTracking().Where(r => r.WarehouseId == request.WarehouseId).Select(r => r.Code).Distinct().ToListAsync(cancellationToken);
        var known = rackCodes.ToDictionary(c => c.ToUpperInvariant(), c => c);
        var missing = codes.Where(c => !known.ContainsKey(c.ToUpperInvariant())).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Rack(s) {string.Join(", ", missing)} do not belong to the selected warehouse.");
        }

        var chosen = codes.Select(c => known[c.ToUpperInvariant()]).ToList();

        // One open count per rack: a second batch over the same rack would count stock that the first is still counting.
        var upper = chosen.Select(c => c.ToUpperInvariant()).ToList();
        var clash = await db.CycleCountBatchRacks.AsNoTracking()
            .Where(r => r.CycleCountBatch.WarehouseId == request.WarehouseId
                && CycleCountRules.OpenStatuses.Contains(r.CycleCountBatch.Status)
                && r.Status != CycleCountRackStatus.Excluded
                && upper.Contains(r.RackCode.ToUpper()))
            .Select(r => new { r.RackCode, r.CycleCountBatch.BatchNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (clash is not null)
        {
            throw new InvalidOperationException($"Rack {clash.RackCode} is already part of the open cycle count {clash.BatchNumber}. Close or drop it first.");
        }

        var warnings = await CycleCountWarnings.BuildAsync(db, request.WarehouseId, cancellationToken);
        if (warnings.Messages.Count > 0 && !request.AcknowledgeWarnings)
        {
            throw new InvalidOperationException(
                "Before scheduling: " + string.Join(" ", warnings.Messages) + " Confirm to schedule anyway.");
        }

        var now = clock.UtcNow;
        CycleCountBatch? batch = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            batch = new CycleCountBatch
            {
                BatchNumber = await CycleCountRules.NextBatchNumberAsync(db, now, cancellationToken),
                WarehouseId = request.WarehouseId,
                ScheduleFrom = request.ScheduleFrom,
                ScheduleTo = request.ScheduleTo,
                Status = CycleCountBatchStatus.Scheduled,
                AutoReleaseFreezeAtScheduleEnd = request.AutoReleaseFreezeAtScheduleEnd,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                CreatedAt = now,
                CreatedByUserId = user.UserId,
                CreatedByUserName = user.UserName,
                RowVersion = 1
            };
            foreach (var code in chosen)
            {
                batch.Racks.Add(new CycleCountBatchRack { RackCode = code, Status = CycleCountRackStatus.Open, RowVersion = 1 });
            }

            CycleCountRules.AddHistory(db, batch, CycleCountActions.Scheduled, user, now,
                updated: $"{request.ScheduleFrom:yyyy-MM-dd HH:mm} to {request.ScheduleTo:yyyy-MM-dd HH:mm}; racks {string.Join(", ", chosen)}; freeze {(request.AutoReleaseFreezeAtScheduleEnd ? "ends at Schedule To" : "until closed or dropped")}", reason: batch.Remarks);
            db.CycleCountBatches.Add(batch);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another schedule took the same number a moment ago: detach this attempt and take the next number.
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count scheduled",
            $"{batch!.BatchNumber} is scheduled from {request.ScheduleFrom:dd MMM HH:mm} to {request.ScheduleTo:dd MMM HH:mm}. Inward and Pullout creation is blocked for the warehouse from the start time until it is closed.",
            cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

public sealed class ExtendCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<ExtendCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(ExtendCycleCountCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        if (batch.Status is not (CycleCountBatchStatus.Scheduled or CycleCountBatchStatus.InProgress or CycleCountBatchStatus.Extended))
        {
            throw new InvalidOperationException($"A cycle count that is {batch.Status} cannot be extended.");
        }

        if (request.NewScheduleTo <= batch.ScheduleTo)
        {
            throw new InvalidOperationException("The new end must be later than the current Schedule To.");
        }

        var now = clock.UtcNow;
        var previous = batch.ScheduleTo;
        batch.ScheduleTo = request.NewScheduleTo;
        batch.Status = CycleCountBatchStatus.Extended;
        CycleCountRules.Touch(batch, user, now);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.Extended, user, now, $"{previous:yyyy-MM-dd HH:mm}", $"{request.NewScheduleTo:yyyy-MM-dd HH:mm}", request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken);
        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count extended", $"{batch.BatchNumber} now ends {request.NewScheduleTo:dd MMM HH:mm}.", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

public sealed class ReduceCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<ReduceCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(ReduceCycleCountCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        if (batch.Status is not (CycleCountBatchStatus.Scheduled or CycleCountBatchStatus.InProgress or CycleCountBatchStatus.Extended))
        {
            throw new InvalidOperationException($"A cycle count that is {batch.Status} cannot be shortened.");
        }

        if (request.NewScheduleTo >= batch.ScheduleTo)
        {
            throw new InvalidOperationException("The new end must be earlier than the current Schedule To.");
        }

        if (request.NewScheduleTo <= batch.ScheduleFrom)
        {
            throw new InvalidOperationException("The new end must be after Schedule From.");
        }

        var pending = await db.CycleCountCards.CountAsync(
            c => c.CycleCountBatchId == batch.Id && !c.IsExcluded && c.State == CycleCountCardState.Pending, cancellationToken);
        if (pending > 0 && !request.AcknowledgeIncomplete)
        {
            throw new InvalidOperationException(
                $"{pending} count card(s) are still pending. They are kept as they are (nothing is discarded) and the counting can still be finished after the new end. Confirm to reduce anyway.");
        }

        var now = clock.UtcNow;
        var previous = batch.ScheduleTo;
        batch.ScheduleTo = request.NewScheduleTo;
        CycleCountRules.Touch(batch, user, now);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.Reduced, user, now, $"{previous:yyyy-MM-dd HH:mm}", $"{request.NewScheduleTo:yyyy-MM-dd HH:mm}",
            pending > 0 ? $"{request.Reason.Trim()} ({pending} pending card(s) kept)" : request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken);
        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count schedule modified", $"{batch.BatchNumber} now ends {request.NewScheduleTo:dd MMM HH:mm}.", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

public sealed class PartiallyCloseCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<PartiallyCloseCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(PartiallyCloseCycleCountCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        if (batch.Status is not (CycleCountBatchStatus.InProgress or CycleCountBatchStatus.Extended))
        {
            throw new InvalidOperationException("Only a cycle count that has been started can be closed partially.");
        }

        var now = clock.UtcNow;
        var pending = await db.CycleCountCards
            .Where(c => c.CycleCountBatchId == batch.Id && !c.IsExcluded && c.State == CycleCountCardState.Pending && c.SubmittedAt == null)
            .ToListAsync(cancellationToken);
        var excluded = 0;
        foreach (var card in pending)
        {
            await db.Entry(card).ReloadAsync(cancellationToken); // a counter may have counted it since this copy was loaded
            if (card.State != CycleCountCardState.Pending || card.IsExcluded || card.SubmittedAt != null) continue;
            card.IsExcluded = true;
            card.RowVersion++;
            card.UpdatedAt = now;
            card.UpdatedByUserId = user.UserId;
            excluded++;
        }

        // A rack with nothing counted has nothing to submit; a rack with counted cards stays open so its completed part can still be submitted.
        var counted = await db.CycleCountCards.AsNoTracking()
            .Where(c => c.CycleCountBatchId == batch.Id && !c.IsExcluded && c.State != CycleCountCardState.Pending)
            .Select(c => c.RackCode).Distinct().ToListAsync(cancellationToken);
        var countedSet = counted.Select(c => c.ToUpperInvariant()).ToHashSet();
        foreach (var rack in batch.Racks.Where(r => r.Status == CycleCountRackStatus.Open && !countedSet.Contains(r.RackCode.ToUpperInvariant())))
        {
            rack.Status = CycleCountRackStatus.Excluded;
            rack.RowVersion++;
        }

        batch.Status = CycleCountBatchStatus.PartiallyClosed;
        CycleCountRules.Touch(batch, user, now);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.PartiallyClosed, user, now, null, $"{excluded} card(s) excluded", request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken);
        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count partially closed",
            $"{batch.BatchNumber}: {excluded} uncounted card(s) were excluded. Submit the completed racks to apply their counts.", "warning", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

public sealed class DropCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<DropCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(DropCycleCountCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        if (!CycleCountBatch.IsOpen(batch.Status))
        {
            throw new InvalidOperationException($"This cycle count is already {batch.Status}.");
        }

        var now = clock.UtcNow;
        var previous = batch.Status;
        batch.Status = CycleCountBatchStatus.Dropped;
        batch.DropReason = request.Reason.Trim();
        batch.ActualEndAt = now;
        CycleCountRules.Touch(batch, user, now);
        // The batch, its cards and history stay. Counts that were never submitted are simply never applied to stock.
        CycleCountRules.AddHistory(db, batch, CycleCountActions.Dropped, user, now, previous.ToString(), CycleCountBatchStatus.Dropped.ToString(), request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken);
        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count dropped",
            $"{batch.BatchNumber} was dropped. Inward and Pullout creation is allowed again for the warehouse unless another cycle count is open.", "warning", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

public sealed class CloseCycleCountHandler(
    IApplicationDbContext db, IWarehouseAccessService access, IDateTimeProvider clock, ICurrentUserService user, IRealtimeNotificationService notifications)
    : IRequestHandler<CloseCycleCountCommand, CycleCountBatchDto>
{
    public async Task<CycleCountBatchDto> Handle(CloseCycleCountCommand request, CancellationToken cancellationToken)
    {
        var batch = await CycleCountRules.LoadBatchAsync(db, access, request.BatchId, cancellationToken);
        if (!CycleCountBatch.IsOpen(batch.Status))
        {
            throw new InvalidOperationException($"This cycle count is already {batch.Status}.");
        }

        var open = batch.Racks.Where(r => r.Status == CycleCountRackStatus.Open).Select(r => r.RackCode).ToList();
        if (open.Count > 0)
        {
            throw new InvalidOperationException($"Rack(s) {string.Join(", ", open)} are not submitted yet. Submit them, or close the batch partially to exclude the uncounted part.");
        }

        var now = clock.UtcNow;
        var previous = batch.Status;
        batch.Status = CycleCountBatchStatus.Closed;
        batch.ActualEndAt = now;
        if (!string.IsNullOrWhiteSpace(request.Remarks)) batch.Remarks = string.IsNullOrWhiteSpace(batch.Remarks) ? request.Remarks.Trim() : $"{batch.Remarks} | {request.Remarks.Trim()}";
        CycleCountRules.Touch(batch, user, now);
        CycleCountRules.AddHistory(db, batch, CycleCountActions.Closed, user, now, previous.ToString(), CycleCountBatchStatus.Closed.ToString(), request.Remarks);
        await db.SaveChangesAsync(cancellationToken);
        await CycleCountNotify.PublishAsync(notifications, now, "Cycle count closed",
            $"{batch.BatchNumber} is closed. Inward and Pullout creation is allowed again for the warehouse unless another cycle count is open.", cancellationToken: cancellationToken, excludeUserId: user.UserId);
        return (await CycleCountRules.ToDtosAsync(db, [batch], cancellationToken))[0];
    }
}

internal static class CycleCountWarnings
{
    /// <summary>Orders that are still in flight and could change the stock being counted.</summary>
    public static async Task<CycleCountScheduleWarningsDto> BuildAsync(IApplicationDbContext db, int warehouseId, CancellationToken cancellationToken)
    {
        var inward = await db.InwardTransactions.AsNoTracking()
            .CountAsync(t => t.WarehouseId == warehouseId && !t.IsRejected
                && t.Lines.Any(l => l.Putaways.Any(p => !p.IsPickerPlaced && p.ExcludedLocation == null)), cancellationToken);
        var pullouts = await db.PulloutTransactions.AsNoTracking()
            .CountAsync(t => t.WarehouseId == warehouseId && !t.IsRejected && t.IsConfirmed && t.ConfirmedAt == null, cancellationToken);
        var messages = new List<string>();
        if (inward > 0) messages.Add($"{inward} inward order(s) still have put-aways the picker has not placed.");
        if (pullouts > 0) messages.Add($"{pullouts} pullout(s) are still being picked; their picks can move stock that is being counted.");
        return new CycleCountScheduleWarningsDto(inward, pullouts, messages);
    }
}
