using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Submits one or more inward (goods-receipt) lines and reserves suggested put-away
/// locations. Physical stock is written only after operator confirmation.
/// </summary>
public sealed record SubmitInwardCommand(IReadOnlyList<SubmitInwardLine> Lines) : IRequest<InwardResultDto>;

public sealed record SubmitInwardLine(long MaterialCode, decimal QuantityBoxes, string MfgMonthText);

public sealed class SubmitInwardValidator : AbstractValidator<SubmitInwardCommand>
{
    public SubmitInwardValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialCode).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).GreaterThan(0);
            line.RuleFor(l => l.MfgMonthText)
                .Must(text => MfgMonthParser.TryParse(text, out _))
                .WithMessage("Mfg Month must look like 'MAR|2010'.");
        });
    }
}

public sealed class SubmitInwardHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<SubmitInwardCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(SubmitInwardCommand request, CancellationToken cancellationToken)
    {
        var materialCodes = request.Lines.Select(l => l.MaterialCode).ToList();
        var materialsByCode = await db.Materials
            .Where(m => materialCodes.Contains(m.MaterialNumber))
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);

        var transaction = new InwardTransaction
        {
            ReferenceNumber = $"GRN-{clock.UtcNow:yyyyMMdd-HHmmss}",
            CreatedAt = clock.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        db.InwardTransactions.Add(transaction);
        var resultLines = new List<InwardLineResultDto>();

        foreach (var line in request.Lines)
        {
            if (!materialsByCode.TryGetValue(line.MaterialCode, out var material))
            {
                var mfgMonthLabel = MfgMonthParser.TryParse(line.MfgMonthText, out var unknownMaterialMfgMonth)
                    ? MfgMonthParser.Format(unknownMaterialMfgMonth)
                    : line.MfgMonthText;
                resultLines.Add(new InwardLineResultDto(
                    line.MaterialCode, "(unknown material)", mfgMonthLabel, line.QuantityBoxes, 0,
                    Domain.Enums.AllocationStatus.Failed, "Material code not found in Material Master.", [], IsConfirmed: true));
                continue;
            }

            MfgMonthParser.TryParse(line.MfgMonthText, out var mfgMonth);

            var (transactionLine, reservedRows, allocation) =
                await AllocateAndReserveLineAsync(transaction, material, line.QuantityBoxes, mfgMonth, cancellationToken);

            resultLines.Add(new InwardLineResultDto(
                material.MaterialNumber,
                material.Description,
                MfgMonthParser.Format(mfgMonth),
                line.QuantityBoxes,
                allocation.AllocatedQuantityBoxes,
                allocation.Status,
                allocation.Remarks,
                reservedRows
                    .OrderBy(r => r.LocationCode)
                    .Select(r => new InwardAllocationLineDto(
                        r.LocationCode,
                        r.Putaway.QuantityBoxes,
                        r.Reason,
                        IsConfirmed: r.Putaway.IsConfirmed,
                        PutawayId: r.Putaway.Id))
                    .ToList(),
                IsConfirmed: transactionLine.Putaways.Count == 0));
        }

        await db.SaveChangesAsync(cancellationToken);

        return new InwardResultDto(transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt, resultLines, IsConfirmed: resultLines.All(l => l.IsConfirmed));
    }

    private const int MaxAllocationAttempts = 3;

    /// <summary>
    /// Runs the allocation engine, reserves the resulting locations, and saves - retrying if a
    /// concurrent submission reserved against the same position(s) first. Each pallet position
    /// touched by a reservation has its <see cref="PalletPosition.RowVersion"/> incremented, so a
    /// losing concurrent save fails with <see cref="DbUpdateConcurrencyException"/> instead of
    /// silently overbooking the location; the loser discards its attempt and re-runs allocation
    /// against the now-current state (which will see the winner's reservation and route around it).
    ///
    /// This relies on <c>SaveChangesAsync</c> being atomic - a thrown <see cref="DbUpdateConcurrencyException"/>
    /// means NONE of this attempt's changes (including the newly-Added line/putaways) were
    /// persisted, which is a standard guarantee of every EF Core relational provider, including
    /// the Pomelo MySQL provider this app runs on in production. (EF Core's InMemory *test*
    /// provider does not honor this for a mixed Added+Modified batch, which is why this specific
    /// retry-recovers-cleanly path is verified by this comment and by relational-provider
    /// documentation rather than by an InMemory-backed test - see SubmitInwardConcurrencyTests.)
    /// </summary>
    private async Task<(InwardTransactionLine Line, List<(InwardPutaway Putaway, string LocationCode, string? Reason)> Reserved, InwardAllocationResult Allocation)>
        AllocateAndReserveLineAsync(InwardTransaction transaction, Material material, decimal quantityBoxes, int mfgMonth, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAllocationAttempts; attempt++)
        {
            var allocation = await allocator.AllocateAsync(material.Id, quantityBoxes, mfgMonth, cancellationToken: cancellationToken);

            var transactionLine = new InwardTransactionLine
            {
                MaterialId = material.Id,
                MfgMonth = mfgMonth,
                RequestedQuantityBoxes = quantityBoxes,
                AllocatedQuantityBoxes = allocation.AllocatedQuantityBoxes,
                Status = allocation.Status,
                Remarks = allocation.Remarks
            };

            var reservedRows = new List<(InwardPutaway Putaway, string LocationCode, string? Reason)>();

            foreach (var putaway in allocation.Lines)
            {
                var position = await db.PalletPositions
                    .FirstAsync(p => p.LocationCode == putaway.LocationCode, cancellationToken);
                position.RowVersion++;

                var reservedPutaway = new InwardPutaway
                {
                    PalletPositionId = position.Id,
                    QuantityBoxes = putaway.QuantityBoxes,
                    AllocationReason = putaway.Reason,
                    IsConfirmed = false
                };

                transactionLine.Putaways.Add(reservedPutaway);
                reservedRows.Add((reservedPutaway, putaway.LocationCode, putaway.Reason));
            }

            transaction.Lines.Add(transactionLine);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return (transactionLine, reservedRows, allocation);
            }
            catch (DbUpdateConcurrencyException)
            {
                transaction.Lines.Remove(transactionLine);
                db.Entry(transactionLine).State = EntityState.Detached;
                foreach (var (putaway, _, _) in reservedRows)
                {
                    db.Entry(putaway).State = EntityState.Detached;
                }

                foreach (var entry in db.ChangeTracker.Entries<PalletPosition>().Where(e => e.State == EntityState.Modified).ToList())
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            "Unable to reserve a put-away location because of high concurrent demand on the same location(s). Please retry.");
    }
}
