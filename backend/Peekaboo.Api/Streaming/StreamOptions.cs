using System.Globalization;

namespace Peekaboo.Api.Streaming;

public sealed record StreamOptions(
    string FfmpegPath,
    int MaxStreams,
    int MaxViewersPerStream,
    TimeSpan StartupTimeout,
    TimeSpan IdleTimeout,
    string RtspTransport)
{
    public static StreamOptions FromConfiguration(IConfiguration configuration)
    {
        var ffmpegPath = configuration["PEEKABOO_FFMPEG_PATH"];
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            ffmpegPath = "ffmpeg";
        }

        var maxStreams = ParseBounded(
            configuration["PEEKABOO_MAX_STREAMS"],
            defaultValue: 9,
            "PEEKABOO_MAX_STREAMS");

        var maxViewersPerStream = ParseBounded(
            configuration["PEEKABOO_MAX_VIEWERS_PER_STREAM"],
            defaultValue: 8,
            "PEEKABOO_MAX_VIEWERS_PER_STREAM");

        var rtspTransport = configuration["PEEKABOO_RTSP_TRANSPORT"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(rtspTransport))
        {
            rtspTransport = "tcp";
        }

        if (rtspTransport is not ("tcp" or "udp"))
        {
            throw new InvalidOperationException("PEEKABOO_RTSP_TRANSPORT must be 'tcp' or 'udp'.");
        }

        return new StreamOptions(
            ffmpegPath,
            maxStreams,
            maxViewersPerStream,
            StartupTimeout: TimeSpan.FromSeconds(20),
            IdleTimeout: TimeSpan.FromSeconds(30),
            rtspTransport);
    }

    private static int ParseBounded(string? value, int defaultValue, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed is < 1 or > 64)
        {
            throw new InvalidOperationException($"{settingName} must be an integer between 1 and 64.");
        }

        return parsed;
    }
}
