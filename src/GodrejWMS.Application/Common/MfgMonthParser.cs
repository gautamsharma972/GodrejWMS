using System.Globalization;

namespace GodrejWMS.Application.Common;

/// <summary>
/// Parses/formats the "MMM|yyyy" manufacturing-month label used throughout the source workbook
/// (e.g. "MAR|2026"), converting to/from the compact yyyyMM integer the domain stores.
/// </summary>
public static class MfgMonthParser
{
    private static readonly string[] Formats = ["MMM|yyyy", "MMM|yy", "MM/yyyy", "yyyy-MM"];

    public static bool TryParse(string? text, out int mfgMonth)
    {
        mfgMonth = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().ToUpperInvariant();

        foreach (var format in Formats)
        {
            if (DateOnly.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                mfgMonth = ToStorageValue(parsed.Year, parsed.Month);
                return true;
            }
        }

        if (trimmed.Length == 6 &&
            int.TryParse(trimmed, out var compact) &&
            IsValid(compact))
        {
            mfgMonth = compact;
            return true;
        }

        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var generic))
        {
            mfgMonth = ToStorageValue(generic.Year, generic.Month);
            return true;
        }

        return false;
    }

    public static string Format(int mfgMonth)
    {
        var date = ToDateOnly(mfgMonth);
        return $"{date:MMM}|{date:yyyy}".ToUpperInvariant();
    }

    public static int ToStorageValue(int year, int month) => (year * 100) + month;

    public static DateOnly ToDateOnly(int mfgMonth) => new(mfgMonth / 100, mfgMonth % 100, 1);

    private static bool IsValid(int mfgMonth)
    {
        var year = mfgMonth / 100;
        var month = mfgMonth % 100;
        return year is >= 1900 and <= 9999 && month is >= 1 and <= 12;
    }
}
