namespace Peekaboo.Api.Streaming;

/// <summary>
/// An upstream video source for one stream session. Every source must identify
/// its playback status and optional maximum duration so the session can enforce
/// the existing watchdog and stream-limit behavior.
/// </summary>
public abstract record StreamSource
{
    public abstract bool IsPlayback { get; }

    public abstract TimeSpan? MaximumDuration { get; }
}

/// <summary>
/// An RTSP URL whose credentials are injected only at process start. The URL must
/// never contain credentials when it is stored in this source.
/// </summary>
public sealed record RtspStreamSource(
    string Url,
    bool Playback = false,
    TimeSpan? Duration = null) : StreamSource
{
    public override bool IsPlayback => Playback;

    public override TimeSpan? MaximumDuration => Duration;
}

/// <summary>
/// Canonical stream-kind values used in <see cref="StreamKey"/>. Keys are compared
/// by exact string equality, so kinds must be created through these constants.
/// </summary>
public static class StreamKinds
{
    public const string Main = "main";
    public const string Sub = "sub";
    public const string PlaybackPrefix = "playback:";
}
