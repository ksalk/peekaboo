using Peekaboo.Api.Alarms;
using Xunit;

namespace Peekaboo.Tests.Alarms;

public sealed class AlarmTimeRangeTests
{
    [Theory]
    [InlineData("2026-11-21T00:00:00Z", "2026-11-22T00:00:00Z")]
    [InlineData("2026-11-21T00:00:00.000Z", "2026-11-22T00:00:00.000Z")]
    [InlineData("2026-11-21T01:00:00+01:00", "2026-11-22T01:00:00+01:00")]
    public void TryParse_accepts_iso8601_offsets(string start, string end)
    {
        Assert.True(AlarmTimeRange.TryParse(start, end, out _, out _, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_accepts_the_thirty_one_day_boundary()
    {
        Assert.True(AlarmTimeRange.TryParse(
            "2026-10-22T00:00:00Z",
            "2026-11-22T00:00:00Z",
            out _,
            out _,
            out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_rejects_more_than_thirty_one_days()
    {
        Assert.False(AlarmTimeRange.TryParse(
            "2026-10-21T23:59:59Z",
            "2026-11-22T00:00:00Z",
            out _,
            out _,
            out var error));
        Assert.Contains("31 days", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_rejects_an_empty_or_reversed_range()
    {
        Assert.False(AlarmTimeRange.TryParse(null, null, out _, out _, out var missing));
        Assert.Contains("required", missing, StringComparison.OrdinalIgnoreCase);

        Assert.False(AlarmTimeRange.TryParse(
            "2026-11-22T00:00:00Z",
            "2026-11-21T00:00:00Z",
            out _,
            out _,
            out var reversed));
        Assert.Contains("after", reversed, StringComparison.OrdinalIgnoreCase);
    }
}
