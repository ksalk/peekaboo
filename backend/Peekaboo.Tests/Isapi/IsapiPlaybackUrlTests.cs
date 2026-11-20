using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Isapi;

public sealed class IsapiPlaybackUrlTests
{
    [Fact]
    public void Build_uses_the_documented_track_path_and_utc_times()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 32, 1, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 20, 10, 40, 42, TimeSpan.Zero);

        var url = IsapiPlaybackUrl.Build(
            "rtsp://192.168.1.64:554/",
            "101",
            start,
            end);

        Assert.Equal(
            "rtsp://192.168.1.64:554/ISAPI/Streaming/tracks/101" +
            "?starttime=20261120T103201Z&endtime=20261120T104042Z",
            url);
    }

    [Fact]
    public void Build_converts_offsets_to_utc()
    {
        var start = new DateTimeOffset(2026, 11, 20, 11, 32, 1, TimeSpan.FromHours(1));
        var end = new DateTimeOffset(2026, 11, 20, 11, 40, 42, TimeSpan.FromHours(1));

        var url = IsapiPlaybackUrl.Build("rtsps://nvr.local:322", "201", start, end);

        Assert.StartsWith(
            "rtsps://nvr.local:322/ISAPI/Streaming/tracks/201?starttime=20261120T103201Z",
            url,
            StringComparison.Ordinal);
        Assert.EndsWith("endtime=20261120T104042Z", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_times_use_iso_8601_utc()
    {
        var value = new DateTimeOffset(2026, 11, 20, 11, 32, 1, TimeSpan.FromHours(1));

        Assert.Equal("2026-11-20T10:32:01Z", IsapiTime.FormatSearch(value));
    }
}
