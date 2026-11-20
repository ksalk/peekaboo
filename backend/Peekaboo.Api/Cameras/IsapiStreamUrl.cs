namespace Peekaboo.Api.Cameras;

/// <summary>
/// Builds Hikvision ISAPI live-view URLs. The stream ID identifies both the channel
/// and stream type: channel number * 100 + stream type (1 main, 2 sub, 3 third).
/// </summary>
public static class IsapiStreamUrl
{
    public const string StreamingPath = "/ISAPI/Streaming/channels";

    public static string Build(string baseUrl, string streamId) =>
        $"{baseUrl.TrimEnd('/')}{StreamingPath}/{streamId}";
}
