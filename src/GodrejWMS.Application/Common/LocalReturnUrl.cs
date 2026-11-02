namespace GodrejWMS.Application.Common;

/// <summary>Remembers which page a user came from, so a Back button can return there instead of a fixed page.
/// The origin travels in a <c>returnUrl</c> query parameter. Only a site-relative path is ever accepted - never an
/// absolute URL, a protocol-relative one (<c>//host</c>) or anything with a scheme - so the parameter cannot be used
/// to send someone to another site.</summary>
public static class LocalReturnUrl
{
    public const string QueryName = "returnUrl";

    /// <summary>The page to go back to: <paramref name="returnUrl"/> when it is a safe local path, else <paramref name="fallback"/>.</summary>
    public static string Safe(string? returnUrl, string fallback) => IsLocalPath(returnUrl) ? returnUrl! : fallback;

    /// <summary>Adds <c>returnUrl</c> to <paramref name="url"/>, or leaves it unchanged when there is nothing safe to add.</summary>
    public static string Append(string url, string? returnUrl) =>
        IsLocalPath(returnUrl)
            ? $"{url}{(url.Contains('?') ? '&' : '?')}{QueryName}={Uri.EscapeDataString(returnUrl!)}"
            : url;

    public static bool IsLocalPath(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value[0] == '/'
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains('\\')
        && !value.Contains("://", StringComparison.Ordinal)
        && !value.Any(char.IsControl);
}
