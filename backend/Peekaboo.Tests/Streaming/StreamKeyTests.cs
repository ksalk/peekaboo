using Peekaboo.Api.Cameras;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class StreamKeyTests
{
    [Fact]
    public void Live_keys_distinguish_main_and_sub_streams()
    {
        var main = StreamKey.Live("camera-1", CameraStream.Main);
        var sub = StreamKey.Live("camera-1", CameraStream.Sub);

        Assert.Equal("camera-1:main", main.ToString());
        Assert.Equal("camera-1:sub", sub.ToString());
        Assert.NotEqual(main, sub);
    }

    [Fact]
    public void Playback_keys_are_marked_as_playback()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var playback = StreamKey.Playback("camera-1", start, start.AddMinutes(5));
        var live = StreamKey.Live("camera-1", CameraStream.Main);

        Assert.True(playback.IsPlayback);
        Assert.False(live.IsPlayback);
    }

    [Fact]
    public void Live_keys_with_the_same_stream_share_the_same_session_identity()
    {
        var first = StreamKey.Live("camera-1", CameraStream.Main);
        var second = StreamKey.Live("camera-1", CameraStream.Main);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Video_only_fallback_uses_a_distinct_key()
    {
        var audio = StreamKey.Live("camera-1", CameraStream.Main);
        var video = StreamKey.Live("camera-1", CameraStream.Main, audioEnabled: false);

        Assert.NotEqual(audio, video);
        Assert.Equal("camera-1:main:video", video.ToString());
    }
}
