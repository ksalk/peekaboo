using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

/// <summary>
/// Builds the RTSP playback URL documented in ISAPI section 15.9.17. The track ID
/// is the camera's main stream ID (channel * 100 + 1). Hikvision firmware expects
/// device-local wall-clock components in this URL.
/// </summary>
public static class IsapiPlaybackUrl
{
    public const string TracksPath = "/ISAPI/Streaming/tracks";

    public static string Build(
        string rtspBaseUrl,
        string trackId,
        DateTimeOffset start,
        DateTimeOffset end,
        INvrTimeZone timeZone) =>
        $"{rtspBaseUrl.TrimEnd('/')}{TracksPath}/{Uri.EscapeDataString(trackId)}" +
        $"?starttime={IsapiTime.FormatPlayback(timeZone.ToDeviceWallTime(start))}" +
        $"&endtime={IsapiTime.FormatPlayback(timeZone.ToDeviceWallTime(end))}";
}
