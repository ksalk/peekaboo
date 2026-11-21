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
