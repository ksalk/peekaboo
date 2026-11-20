namespace Peekaboo.Api.Isapi;

/// <summary>
/// Builds the RTSP playback URL documented in ISAPI section 15.9.17. The track ID
/// is the camera's main stream ID (channel * 100 + 1).
/// </summary>
public static class IsapiPlaybackUrl
{
    public const string TracksPath = "/ISAPI/Streaming/tracks";

    public static string Build(
        string rtspBaseUrl,
        string trackId,
        DateTimeOffset start,
        DateTimeOffset end) =>
        $"{rtspBaseUrl.TrimEnd('/')}{TracksPath}/{Uri.EscapeDataString(trackId)}" +
        $"?starttime={IsapiTime.FormatPlayback(start)}" +
        $"&endtime={IsapiTime.FormatPlayback(end)}";
}
