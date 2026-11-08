using System.Security.Cryptography;
using System.Text;

namespace GodrejWMS.Application.Common;

/// <summary>Fingerprint of an uploaded order (warehouse + header + normalised lines) used to refuse re-posting the same file.</summary>
public static class UploadFingerprint
{
    /// <summary>An upload matching a non-rejected order newer than this is treated as a re-upload of the same file.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(24);

    public static string Compute(int warehouseId, IEnumerable<string?> header, IEnumerable<string> lines)
    {
        var text = new StringBuilder().Append(warehouseId).Append('\n');
        foreach (var part in header)
        {
            text.Append(part?.Trim().ToUpperInvariant()).Append('\n');
        }

        text.Append("--\n");
        foreach (var line in lines.OrderBy(l => l, StringComparer.Ordinal))
        {
            text.Append(line).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
