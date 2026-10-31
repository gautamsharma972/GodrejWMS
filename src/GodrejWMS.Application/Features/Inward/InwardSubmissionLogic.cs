using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>Shared by every way of creating a GRN (today: <c>SubmitInwardCommand</c> - manual
/// entry and the single-sheet/multi-sheet uploads all funnel through it; and the Pickers'
/// <c>SubmitInwardWithoutPkmCommand</c>) so the GRN reference sequence and the allocate-reserve-
/// retry mechanics are identical everywhere rather than duplicated per entry point.</summary>
internal static class InwardSubmissionLogic
{
    /// <summary>Date+sequence reference (e.g. "GRN-20261031-000001") - queries the day's existing
    /// references and takes max+1, so looping this once per line/sheet in a single request never
    /// collides (the former second-precision timestamp reference did).</summary>
    public static async Task<string> NextReferenceAsync(IApplicationDbContext db, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var prefix = $"GRN-{createdAt:yyyyMMdd}-";
        var references = await db.InwardTransactions.AsNoTracking()
            .Where(t => t.ReferenceNumber.StartsWith(prefix))
            .Select(t => t.ReferenceNumber).ToListAsync(cancellationToken);
        var sequence = references.Select(r => int.TryParse(r[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0).Max() + 1;
        if (sequence > 999999)
            throw new InvalidOperationException("The daily GRN reference sequence is exhausted.");
        return $"{prefix}{sequence:D6}";
    }

    private const int MaxAllocationAttempts = 3;

    /// <summary>
    /// Runs the allocation engine, reserves the resulting locations, and saves - retrying if a
    /// concurrent submission reserved against the same position(s) first. Each pallet position
    /// touched by a reservation has its <see cref="PalletPosition.RowVersion"/> incremented, so a
    /// losing concurrent save fails with <see cref="DbUpdateConcurrencyException"/> instead of
    /// silently overbooking the location; the loser discards its attempt and re-runs allocation
    /// against the now-current state (which will see the winner's reservation and route around it).
    /// <paramref name="mfgMonth"/> is null only for the Pickers' "Inward without PKM" flow, which
    /// the allocator routes straight to empty pallet positions - see
    /// <see cref="IPalletAllocationService.AllocateAsync"/>.
    ///
    /// This relies on <c>SaveChangesAsync</c> being atomic - a thrown <see cref="DbUpdateConcurrencyException"/>
    /// means NONE of this attempt's changes (including the newly-Added line/putaways) were
    /// persisted, which is a standard guarantee of every EF Core relational provider, including
    /// the Pomelo MySQL provider this app runs on in production. (EF Core's InMemory *test*
    /// provider does not honor this for a mixed Added+Modified batch, which is why this specific
    /// retry-recovers-cleanly path is verified by this comment and by relational-provider
    /// documentation rather than by an InMemory-backed test - see SubmitInwardConcurrencyTests.)
    /// </summary>
    public static async Task<(InwardTransactionLine Line, List<(InwardPutaway Putaway, string LocationCode, string? Reason)> Reserved, InwardAllocationResult Allocation)>
        AllocateAndReserveLineAsync(
            IApplicationDbContext db, IPalletAllocationService allocator,
            InwardTransaction transaction, Material material, decimal quantityBoxes, int? mfgMonth, int warehouseId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAllocationAttempts; attempt++)
        {
            var allocation = await allocator.AllocateAsync(material.Id, quantityBoxes, mfgMonth, warehouseId, cancellationToken: cancellationToken);

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
