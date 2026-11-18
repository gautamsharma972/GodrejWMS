using System.Text.RegularExpressions;
using GodrejWMS.Application.Features.Inward.Dtos;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>One cell of the printed header block, with the merge span the client's own sheet had.</summary>
public sealed record PrintCell(string Text, int RowSpan, int ColSpan, bool Bold);

/// <summary>The header rows of the sheet as a grid of cells (merged ranges already folded into spans).</summary>
public sealed record PrintHeaderBlock(IReadOnlyList<IReadOnlyList<PrintCell>> Rows, int ColumnCount);

/// <summary>
/// What the printed (A4 landscape) version of a GRN's allocation download shows. The put-away sheet leaves out the Vehicle No. and LR No.
/// (both the table columns and the header-block cells); the unloading sheet keeps the client's header exactly. Pure data, so the print page and
/// the tests share it.
/// </summary>
public static class InwardPrintSheets
{
    public static readonly string[] PutawayHeaders =
    [
        "Material Code", "Material Desc.", "Mfg Month", "Requested (CFB)", "Allocated (CFB)", "Line Status", "Pallet Position", "Qty at Position (CFB)", "Put-away Status"
    ];

    public static readonly string[] UnloadingHeaders = ["Code", "Desc", "Qty", "Inbound Delv No", "Physical Qty", "Pkd/Batch", "Remarks"];

    public static IReadOnlyList<string[]> PutawayRows(InwardDownloadDto download) =>
        download.Rows.Select(r => new[]
        {
            r.MaterialNumber.ToString(), r.MaterialDescription, r.MfgMonthLabel, r.RequestedQuantityBoxes.ToString("0.###"),
            r.AllocatedQuantityBoxes.ToString("0.###"), r.LineStatus, r.LocationCode, r.QuantityBoxes.ToString("0.###"), r.PutawayStatus
        }).ToList();

    public static IReadOnlyList<string[]> UnloadingRows(InwardDownloadDto download) =>
        download.UnloadingRows.Select(r => new[]
        {
            r.Code, r.MaterialDescription, r.RequestedQuantityBoxes.ToString("0.###"), "", "", r.MfgMonthLabel, r.Remarks
        }).ToList();

    private static readonly Regex VehicleOrLr = new(@"^\s*(LR\s*NO|VEHICLE\s*NO)\s*[:\-]*\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The header rows copied from the upload, or null when the GRN has none (a hand-entered one). When
    /// <paramref name="hideVehicleAndLr"/> is set the "LR NO" / "Vehicle No" labels and the values next to them are blanked.</summary>
    public static PrintHeaderBlock? HeaderBlock(InwardDownloadDto download, bool hideVehicleAndLr)
    {
        var block = InwardUploadHeaderBlock.TryParse(download.Header.UploadHeaderJson);
        if (block is null)
        {
            return null;
        }

        var columns = Math.Max(block.Rows.Max(r => r.Count), 1);
        var text = new string[block.Rows.Count, columns];
        for (var r = 0; r < block.Rows.Count; r++)
        {
            for (var c = 0; c < block.Rows[r].Count; c++)
            {
                text[r, c] = block.Rows[r][c] ?? string.Empty;
            }
        }

        if (hideVehicleAndLr)
        {
            for (var r = 0; r < block.Rows.Count; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    if (text[r, c] is { } label && VehicleOrLr.IsMatch(label))
                    {
                        text[r, c] = string.Empty;
                        if (c + 1 < columns)
                        {
                            text[r, c + 1] = string.Empty; // the value written beside the label
                        }
                    }
                }
            }
        }

        // Merged ranges ("A1:F1") become a span on their first cell; the covered cells are skipped.
        var spans = new Dictionary<(int Row, int Col), (int RowSpan, int ColSpan)>();
        var covered = new HashSet<(int Row, int Col)>();
        foreach (var range in block.MergedRanges)
        {
            if (!TryParseRange(range, out var r1, out var c1, out var r2, out var c2) || r1 >= block.Rows.Count || c1 >= columns)
            {
                continue;
            }

            r2 = Math.Min(r2, block.Rows.Count - 1);
            c2 = Math.Min(c2, columns - 1);
            spans[(r1, c1)] = (r2 - r1 + 1, c2 - c1 + 1);
            for (var r = r1; r <= r2; r++)
            {
                for (var c = c1; c <= c2; c++)
                {
                    if (r != r1 || c != c1)
                    {
                        covered.Add((r, c));
                    }
                }
            }
        }

        var rows = new List<IReadOnlyList<PrintCell>>();
        for (var r = 0; r < block.Rows.Count; r++)
        {
            var cells = new List<PrintCell>();
            for (var c = 0; c < columns; c++)
            {
                if (covered.Contains((r, c)))
                {
                    continue;
                }

                var value = text[r, c] ?? string.Empty;
                var span = spans.GetValueOrDefault((r, c), (1, 1));
                var isLabel = value.EndsWith(':') || value.EndsWith(":-") || (value.Any(char.IsLetter) && value == value.ToUpperInvariant());
                cells.Add(new PrintCell(value, span.Item1, span.Item2, isLabel));
            }

            rows.Add(cells);
        }

        return new PrintHeaderBlock(rows, columns);
    }

    private static bool TryParseRange(string range, out int r1, out int c1, out int r2, out int c2)
    {
        r1 = c1 = r2 = c2 = 0;
        var match = Regex.Match(range.Trim(), @"^([A-Za-z]+)(\d+):([A-Za-z]+)(\d+)$");
        if (!match.Success)
        {
            return false;
        }

        c1 = ColumnIndex(match.Groups[1].Value);
        r1 = int.Parse(match.Groups[2].Value) - 1;
        c2 = ColumnIndex(match.Groups[3].Value);
        r2 = int.Parse(match.Groups[4].Value) - 1;
        return r1 >= 0 && r2 >= r1 && c2 >= c1;
    }

    private static int ColumnIndex(string letters)
    {
        var index = 0;
        foreach (var ch in letters.ToUpperInvariant())
        {
            index = index * 26 + (ch - 'A' + 1);
        }

        return index - 1;
    }
}
