namespace Peekaboo.Api.Streaming;

/// <summary>
/// The resolved upstream URL for one stream session. The URL never contains
/// credentials; <see cref="StreamSession"/> injects them when ffmpeg starts.
/// </summary>
public sealed record StreamSource(string Url);

/// <summary>
/// Canonical stream-kind values used in <see cref="StreamKey"/>. Keys are compared
/// by exact string equality, so kinds must be created through these constants.
/// </summary>
public static class StreamKinds
{
    public const string Main = "main";
    public const string Sub = "sub";
}
