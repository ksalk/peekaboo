using Peekaboo.Api.Cameras;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Nvrs;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class StreamSourceFactoryTests
{
    private readonly StreamSourceFactory _factory = new();

    [Fact]
    public void Rtsp_nvrs_resolve_to_their_stored_url()
    {
        var target = Target(
            NvrTransports.Rtsp,
            streamId: "101",
            url: "rtsp://192.168.10.60:554/ISAPI/Streaming/channels/101");

        var source = Assert.IsType<RtspStreamSource>(_factory.CreateLive(target));

        Assert.Equal(target.Url, source.Url);
        Assert.False(source.IsPlayback);
    }

    [Theory]
    [InlineData("101", 1, 0)]
    [InlineData("102", 1, 1)]
    [InlineData("1601", 16, 0)]
    public void Sdk_nvrs_parse_the_stream_id_into_channel_and_type(
        string streamId,
        int expectedChannel,
        int expectedStreamType)
    {
        var target = Target(NvrTransports.HikvisionSdk, streamId, url: "unused");

        var source = Assert.IsType<HikvisionLiveStreamSource>(_factory.CreateLive(target));

        Assert.Equal("nvr-1", source.NvrId);
        Assert.Equal("192.168.10.60", source.Host);
        Assert.Equal(8001, source.SdkPort);
        Assert.Equal(expectedChannel, source.IsapiChannel);
        Assert.Equal(expectedStreamType, source.StreamType);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("103")]
    [InlineData("abc")]
    public void Invalid_sdk_stream_ids_are_rejected(string streamId)
    {
        var target = Target(NvrTransports.HikvisionSdk, streamId, url: "unused");

        var exception = Assert.Throws<HikvisionException>(
            () => _factory.CreateLive(target));

        Assert.Equal(HikvisionFailureKind.Channel, exception.Kind);
    }

    [Fact]
    public void Unknown_transports_are_rejected()
    {
        var target = Target("bogus", "101", url: "unused");

        var exception = Assert.Throws<HikvisionException>(
            () => _factory.CreateLive(target));

        Assert.Equal(HikvisionFailureKind.Unavailable, exception.Kind);
    }

    private static CameraStreamTarget Target(string transport, string streamId, string url) =>
        new(
            CameraId: "camera-1",
            CameraName: "Camera",
            NvrId: "nvr-1",
            NvrTransport: transport,
            NvrHost: "192.168.10.60",
            SdkPort: 8001,
            StreamId: streamId,
            Stream: CameraStream.Sub,
            Url: url);
}
