using GodrejWMS.Application.Common;
using Xunit;

namespace GodrejWMS.Application.Tests;

/// <summary>The Back button on a GRN returns to the page that linked to it. The origin travels in a query string,
/// so it must only ever be a site-relative path - never somewhere else on the internet.</summary>
public class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/inward/history")]
    [InlineData("/inward-no-pkm/history")]
    [InlineData("/reports/inward")]
    [InlineData("/inward/history?page=2")]
    public void Safe_AcceptsASiteRelativePath(string returnUrl) =>
        Assert.Equal(returnUrl, LocalReturnUrl.Safe(returnUrl, "/fallback"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("inward/history")]                 // not rooted
    [InlineData("//evil.example/inward")]          // protocol-relative: would leave the site
    [InlineData("https://evil.example/")]          // absolute
    [InlineData("http://evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\\evil.example")]                // backslash trick some browsers treat as //
    [InlineData("/ok\r\nSet-Cookie: x=1")]         // control characters
    [InlineData("/redirect?to=https://evil.example")]
    public void Safe_RejectsAnythingThatCouldLeaveTheSite_AndFallsBack(string? returnUrl) =>
        Assert.Equal("/inward/history", LocalReturnUrl.Safe(returnUrl, "/inward/history"));

    [Fact]
    public void Append_AddsAnEscapedReturnUrl() =>
        Assert.Equal("/inward/12?returnUrl=%2Finward-no-pkm%2Fhistory", LocalReturnUrl.Append("/inward/12", "/inward-no-pkm/history"));

    [Fact]
    public void Append_UsesAmpersandWhenTheUrlAlreadyHasAQuery() =>
        Assert.Equal("/inward/12?tab=lines&returnUrl=%2Finward%2Fhistory", LocalReturnUrl.Append("/inward/12?tab=lines", "/inward/history"));

    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example")]
    public void Append_LeavesTheUrlUnchanged_WhenThereIsNothingSafeToRemember(string? returnUrl) =>
        Assert.Equal("/inward/12", LocalReturnUrl.Append("/inward/12", returnUrl));

    [Fact]
    public void AnAppendedReturnUrl_SurvivesTheRoundTrip_AndIsAcceptedBySafe()
    {
        var linked = LocalReturnUrl.Append("/inward/12", "/inward/history");
        var query = System.Web.HttpUtility.ParseQueryString(linked[(linked.IndexOf('?') + 1)..]);

        Assert.Equal("/inward/history", LocalReturnUrl.Safe(query[LocalReturnUrl.QueryName], "/elsewhere"));
    }
}
