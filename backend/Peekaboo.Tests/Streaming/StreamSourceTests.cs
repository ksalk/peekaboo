using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class StreamSourceTests
{
    [Fact]
    public void Rtsp_sources_default_to_live_without_a_duration_cap()
    {
        var source = new RtspStreamSource("rtsp://nvr.local/ISAPI/Streaming/channels/101");

        Assert.False(source.IsPlayback);
        Assert.Null(source.MaximumDuration);
    }

    [Fact]
    public void Hikvision_live_sources_are_live_and_have_a_credential_free_description()
    {
        var source = new HikvisionLiveStreamSource(
            "nvr-1",
            "192.168.10.60",
            8000,
            IsapiChannel: 1,
            StreamType: 1);

        Assert.False(source.IsPlayback);
        Assert.Null(source.MaximumDuration);
        Assert.DoesNotContain("CAMERA_PASSWORD", source.SafeDescription, StringComparison.Ordinal);
        Assert.Contains("192.168.10.60:8000", source.SafeDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void Hikvision_playback_sources_carry_the_range_and_playback_marker()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(5);
        var source = new HikvisionPlaybackStreamSource(
            "nvr-1",
            "192.168.10.60",
            8000,
            IsapiChannel: 1,
            start,
            end);

        Assert.True(source.IsPlayback);
        Assert.Equal(TimeSpan.FromMinutes(5), source.MaximumDuration);
        Assert.Contains("playback", source.SafeDescription, StringComparison.Ordinal);
        Assert.Contains("2026-11-20", source.SafeDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void Rtsp_playback_sources_carry_their_marker_and_duration()
    {
        var duration = TimeSpan.FromMinutes(5);
        var source = new RtspStreamSource(
            "rtsp://nvr.local/ISAPI/Streaming/tracks/101?starttime=20261120T100000Z",
            Playback: true,
            Duration: duration);

        Assert.True(source.IsPlayback);
        Assert.Equal(duration, source.MaximumDuration);
    }
}
