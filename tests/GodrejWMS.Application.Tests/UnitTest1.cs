using GodrejWMS.Application.Common;

namespace GodrejWMS.Application.Tests;

public class MfgMonthParserTests
{
    [Theory]
    [InlineData("MAR|2026", 202603)]
    [InlineData("mar|2026", 202603)]
    [InlineData("DEC|2025", 202512)]
    [InlineData("202603", 202603)]
    public void TryParse_ParsesWorkbookFormat(string text, int expected)
    {
        var success = MfgMonthParser.TryParse(text, out var result);

        Assert.True(success);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a date")]
    public void TryParse_ReturnsFalse_ForInvalidInput(string? text)
    {
        var success = MfgMonthParser.TryParse(text, out _);

        Assert.False(success);
    }

    [Fact]
    public void Format_RoundTripsThroughTryParse()
    {
        var label = MfgMonthParser.Format(202603);
        var success = MfgMonthParser.TryParse(label, out var parsed);

        Assert.True(success);
        Assert.Equal("MAR|2026", label);
        Assert.Equal(202603, parsed);
    }
}
