using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
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
            end,
            TestNvrTimeZones.Utc);

        Assert.Equal(
            "rtsp://192.168.1.64:554/ISAPI/Streaming/tracks/101" +
            "?starttime=20261120T103201Z&endtime=20261120T104042Z",
            url);
    }

    [Fact]
    public void Build_converts_utc_to_the_device_wall_clock()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 32, 1, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 20, 10, 40, 42, TimeSpan.Zero);
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        var url = IsapiPlaybackUrl.Build("rtsps://nvr.local:322", "201", start, end, zone);

        Assert.Equal(
            "rtsps://nvr.local:322/ISAPI/Streaming/tracks/201" +
            "?starttime=20261120T113201Z&endtime=20261120T114042Z",
            url);
    }

    [Fact]
    public void Build_handles_a_zone_with_a_large_offset()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 32, 1, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 20, 10, 40, 42, TimeSpan.Zero);
        var zone = new FixedNvrTimeZone("JST", TimeSpan.FromHours(9));

        var url = IsapiPlaybackUrl.Build("rtsp://nvr.local:554", "101", start, end, zone);

        Assert.Contains("starttime=20261120T193201Z", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_wall_times_use_the_compact_iso_format()
    {
        Assert.Equal(
            "2026-11-20T10:32:01Z",
            IsapiTime.FormatSearch(new DateTime(2026, 11, 20, 10, 32, 1)));
        Assert.Equal(
            "20261120T103201Z",
            IsapiTime.FormatPlayback(new DateTime(2026, 11, 20, 10, 32, 1)));
    }
}
