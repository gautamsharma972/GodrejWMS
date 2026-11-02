using System.Text.Json;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>The first <see cref="RowCount"/> rows of an uploaded Unloading Sheet, captured cell by cell
/// as the client's own display text, plus any merged ranges in those rows (such as the centred
/// "UNLOADING SHEET" title), so the GRN Excel download can reproduce them at the top. Stored as JSON
/// in <c>InwardTransaction.UploadHeaderJson</c>.</summary>
public sealed record InwardUploadHeaderBlock(IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string> MergedRanges)
{
    /// <summary>How many rows of the original upload are repeated at the top of the download.</summary>
    public const int RowCount = 7;

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Null for missing or unreadable JSON, so a bad stored value just means "no header block"
    /// rather than failing a download.</summary>
    public static InwardUploadHeaderBlock? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InwardUploadHeaderBlock>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
