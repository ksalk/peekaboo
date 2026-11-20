using Peekaboo.Api.Cameras;
using Xunit;

namespace Peekaboo.Tests.Cameras;

public sealed class IsapiStreamUrlTests
{
    [Theory]
    [InlineData("rtsp://192.168.1.64:554", "101", "rtsp://192.168.1.64:554/ISAPI/Streaming/channels/101")]
    [InlineData("rtsp://192.168.1.64:554/", "102", "rtsp://192.168.1.64:554/ISAPI/Streaming/channels/102")]
    [InlineData("rtsps://nvr.local:322", "1601", "rtsps://nvr.local:322/ISAPI/Streaming/channels/1601")]
    public void Builds_the_expected_isapi_live_view_url(
        string baseUrl,
        string streamId,
        string expected)
    {
        Assert.Equal(expected, IsapiStreamUrl.Build(baseUrl, streamId));
    }
}
