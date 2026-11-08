using GodrejWMS.Application.Common;
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

    /// <summary>Runs one inward order in one transaction. A concurrent upload that took the same location is detected through the
    /// location / stock row versions; the whole order is then rolled back and run again against the new state.</summary>
    public static async Task<T> RunOrderAsync<T>(IUnitOfWork uow, Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAllocationAttempts; attempt++)
        {
            try
            {
                return await uow.ExecuteInTransactionAsync(body, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAllocationAttempts)
            {
                await Task.Delay(Random.Shared.Next(10, 60) * attempt, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                break;
            }
        }

        throw new InvalidOperationException(
            "Unable to place this order because of high concurrent demand on the same location(s). Please retry.");
    }

    /// <summary>The reference of a non-rejected inward order of the same warehouse with the same upload fingerprint inside the duplicate window, if any.</summary>
    public static async Task<string?> FindDuplicateAsync(
        IApplicationDbContext db, int warehouseId, string hash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now - UploadFingerprint.DuplicateWindow;
        return await db.InwardTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == warehouseId && t.UploadHash == hash && !t.IsRejected && t.CreatedAt >= since)
            .Select(t => t.ReferenceNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
