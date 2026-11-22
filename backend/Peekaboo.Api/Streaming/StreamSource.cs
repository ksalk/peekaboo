using Peekaboo.Api.Nvrs;

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

    /// <summary>
    /// A credential-free description suitable for logs and error redaction.
    /// </summary>
    public abstract string SafeDescription { get; }
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

    public override string SafeDescription => Url;
}

/// <summary>
/// A live channel pulled through the Hikvision SDK private protocol. The ISAPI
/// channel is the logical channel from the stored stream ID (101 -> 1); the SDK
/// channel is resolved from the device's digital-channel numbering at login time.
/// </summary>
public sealed record HikvisionLiveStreamSource(
    string NvrId,
    string Host,
    int SdkPort,
    int IsapiChannel,
    int StreamType) : StreamSource
{
    public override bool IsPlayback => false;

    public override TimeSpan? MaximumDuration => null;

    public override string SafeDescription =>
        $"Hikvision SDK {Host}:{SdkPort} channel {IsapiChannel} stream {StreamType}";
}

/// <summary>
/// A recorded range pulled through the Hikvision SDK private protocol. Playback
/// uses the main stream of the logical ISAPI channel.
/// </summary>
public sealed record HikvisionPlaybackStreamSource(
    string NvrId,
    string Host,
    int SdkPort,
    int IsapiChannel,
    DateTimeOffset Start,
    DateTimeOffset End,
    INvrTimeZone TimeZone) : StreamSource
{
    public override bool IsPlayback => true;

    public override TimeSpan? MaximumDuration => End - Start;

    public override string SafeDescription =>
        $"Hikvision SDK {Host}:{SdkPort} playback channel {IsapiChannel} " +
        $"{Start:O}..{End:O}";
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
