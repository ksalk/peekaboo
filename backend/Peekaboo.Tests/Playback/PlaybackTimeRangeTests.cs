using Peekaboo.Api.Playback;
using Xunit;

namespace Peekaboo.Tests.Playback;

public sealed class PlaybackTimeRangeTests
{
    [Theory]
    [InlineData("2026-11-20T10:00:00Z")]
    [InlineData("2026-11-20T10:00:00.123Z")]
    [InlineData("2026-11-20T10:00:00+01:00")]
    [InlineData("2026-11-20T10:00:00.123+01:00")]
    public void Accepts_iso_8601_timestamps(string start)
    {
        var parsed = PlaybackTimeRange.TryParse(
            start,
            "2026-11-21T00:00:00Z",
            out _,
            out _,
            out var error);

        Assert.True(parsed, error);
    }

    [Theory]
    [InlineData("11/20/2026 10:00:00Z")]
    [InlineData("Nov 20, 2026 10:00Z")]
    [InlineData("2026-11-20 10:00:00Z")]
    [InlineData("")]
    public void Rejects_non_iso_timestamps(string start)
    {
        var parsed = PlaybackTimeRange.TryParse(
            start,
            "2026-12-01T00:00:00Z",
            out _,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void Allows_exactly_31_days()
    {
        var parsed = PlaybackTimeRange.TryParse(
            "2026-11-01T00:00:00Z",
            "2026-12-02T00:00:00Z",
            out _,
            out _,
            out var error);

        Assert.True(parsed, error);
    }

    [Fact]
    public void Rejects_more_than_31_days()
    {
        var parsed = PlaybackTimeRange.TryParse(
            "2026-11-01T00:00:00Z",
            "2026-12-02T00:00:01Z",
            out _,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("31", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_reversed_ranges()
    {
        var parsed = PlaybackTimeRange.TryParse(
            "2026-11-20T10:00:00Z",
            "2026-11-20T10:00:00Z",
            out _,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("after", error, StringComparison.OrdinalIgnoreCase);
    }
}
