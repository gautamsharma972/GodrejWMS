using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Saves a pending preview, refreshes an existing draft, or confirms reviewed picks.</summary>
public sealed record SubmitPulloutCommand(IReadOnlyList<SubmitPulloutLine> Lines,
    PulloutResultDto? ConfirmedPreview = null, string? RefreshReference = null) : IRequest<PulloutResultDto>;

public sealed record SubmitPulloutLine(long MaterialCode, decimal QuantityBoxes);

public sealed class SubmitPulloutValidator : AbstractValidator<SubmitPulloutCommand>
{
    public SubmitPulloutValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialCode).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).GreaterThan(0);
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
            lines = savedPreview.Lines.Select(l => new SubmitPulloutLine(l.MaterialNumber, l.RequestedQuantityBoxes)).ToList();
        }
        else
        {
            var createdAt = clock.UtcNow;
            transaction = new PulloutTransaction
            {
                ReferenceNumber = await NextReferenceAsync(createdAt, cancellationToken), CreatedAt = createdAt,
                CreatedByUserId = currentUser.UserId, IsConfirmed = false
            };
        }

        var materials = await db.Materials.AsNoTracking()
            .Where(m => lines.Select(l => l.MaterialCode).Contains(m.MaterialNumber))
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);
        var resultLines = new List<PulloutLineResultDto>();
        var proposedLines = new List<PulloutTransactionLine>();
        foreach (var line in lines.GroupBy(l => l.MaterialCode)
            .Select(g => new SubmitPulloutLine(g.Key, g.Sum(l => l.QuantityBoxes))))
        {
            if (!materials.TryGetValue(line.MaterialCode, out var material))
            {
                resultLines.Add(new(line.MaterialCode, "(unknown material)", line.QuantityBoxes, 0,
                    Domain.Enums.AllocationStatus.Failed, "Material code not found in Material Master.", []));
                continue;
            }
            var allocation = await allocator.AllocateAsync(material.Id, line.QuantityBoxes, cancellationToken, preview: true);
            var transactionLine = new PulloutTransactionLine
            {
                MaterialId = material.Id, RequestedQuantityBoxes = line.QuantityBoxes,
                PickedQuantityBoxes = allocation.PickedQuantityBoxes, Status = allocation.Status, Remarks = allocation.Remarks
            };
            foreach (var pick in allocation.Picks)
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
                allocation.PickedQuantityBoxes, allocation.Status, allocation.Remarks,
                allocation.Picks.Select(p => new PulloutPickDto(p.LocationCode, MfgMonthParser.Format(p.MfgMonth), p.QuantityBoxes)).ToList()));
        }
        var result = new PulloutResultDto(transaction.ReferenceNumber, transaction.CreatedAt, resultLines);
        if (request.ConfirmedPreview is not null)
        {
            if (!SamePicks(result, request.ConfirmedPreview) || !SamePicks(savedPreview!, request.ConfirmedPreview))
                throw new InvalidOperationException("Stock has changed. Refresh the saved picks, review them, then confirm again.");
            if (!result.Lines.Any(l => l.PickedQuantityBoxes > 0))
                throw new InvalidOperationException("No pickable stock is available. Refresh the picks when stock is available.");
        }

        try
        {
            if (request.ConfirmedPreview is not null)
            {
                // Reload tracked stock before allocation; optimistic concurrency protects the subsequent save.
                foreach (var entry in db.ChangeTracker.Entries<StockBatch>().ToList())
                    entry.State = EntityState.Detached;
                foreach (var line in result.Lines.Where(l => materials.ContainsKey(l.MaterialNumber)))
                {
                    var allocation = await allocator.AllocateAsync(materials[line.MaterialNumber].Id,
                        line.RequestedQuantityBoxes, cancellationToken);
                    var actual = line with { PickedQuantityBoxes = allocation.PickedQuantityBoxes,
                        Picks = allocation.Picks.Select(p => new PulloutPickDto(p.LocationCode, MfgMonthParser.Format(p.MfgMonth), p.QuantityBoxes)).ToList() };
                    if (!SamePicks(result with { Lines = [line] }, result with { Lines = [actual] }))
                        throw new InvalidOperationException("Stock has changed. Refresh the saved picks and confirm again.");
                }
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
            }
            transaction.RowVersion++;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    break;
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
