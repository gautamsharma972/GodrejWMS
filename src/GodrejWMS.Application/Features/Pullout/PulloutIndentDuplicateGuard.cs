using System.Text.RegularExpressions;
using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout;

/// <summary>Shared by every way of bringing a Loading Sheet's "Indent" field into a pullout (Excel
/// upload, paste-from-Excel) so a re-submission of an order already turned into a pullout - the
/// real-world case of a same-day workbook that keeps growing by one sheet per order and gets
/// re-uploaded/re-pasted whole each time - is recognized the same way everywhere, not duplicated
/// per entry point.</summary>
public static partial class PulloutIndentDuplicateGuard
{
    /// <summary>The Indent field can hold our own previously-written
    /// <see cref="GodrejWMS.Domain.Entities.PulloutTransaction.ReferenceNumber"/> (written back onto
    /// it on download), the client's own order/indent number, or both stacked on separate lines (if
    /// the client adds their own note to a sheet/paste we already processed and handed back). Every
    /// non-blank line is checked against both. Throws <see cref="InvalidOperationException"/> with a
    /// friendly message if a match is found; otherwise returns whichever token is the client's own
    /// number (not shaped like our own reference), to be stored on the new transaction so a
    /// *future* submission of this same order is caught too.</summary>
    public static async Task<string?> CheckAsync(IApplicationDbContext db, string? indentText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indentText))
        {
            return null;
        }

        var tokens = indentText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToUpperInvariant())
            .Distinct()
            .ToList();

        if (tokens.Count == 0)
        {
            return null;
        }

        var existing = await db.PulloutTransactions
            .Where(t => !t.IsRejected
                && (tokens.Contains(t.ReferenceNumber.ToUpper())
                    || (t.ClientIndentNumber != null && tokens.Contains(t.ClientIndentNumber.ToUpper()))))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.ReferenceNumber, t.CreatedAt, t.IsConfirmed })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"This order was already pulled as {existing.ReferenceNumber} on " +
                $"{existing.CreatedAt.ToLocalTime():dd MMM yyyy, hh:mm tt}" +
                $"{(existing.IsConfirmed ? " and confirmed" : " (not yet confirmed)")}. " +
                "Skipped to avoid creating a duplicate pullout.");
        }

        var ownReference = tokens.FirstOrDefault(OwnReferencePattern().IsMatch);
        var clientIndentNumber = tokens.FirstOrDefault(t => t != ownReference);
        return clientIndentNumber is { Length: > 100 } ? clientIndentNumber[..100] : clientIndentNumber;
    }

    [GeneratedRegex(@"^PICK-\d{8}-\d{6}$")]
    private static partial Regex OwnReferencePattern();
}
