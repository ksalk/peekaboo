namespace Peekaboo.Api.Isapi;

public enum IsapiFailureKind
{
    BadRequest,
    Unauthorized,
    NotFound,
    Unreachable,
    Timeout,
    InvalidResponse,
    DeviceError,
}

public sealed class IsapiException(string message, IsapiFailureKind kind) : Exception(message)
{
    public IsapiFailureKind Kind { get; } = kind;
}

public sealed record IsapiSearchRequest(
    string NvrId,
    string RtspBaseUrl,
    string TrackId,
    DateTimeOffset Start,
    DateTimeOffset End);

public sealed record IsapiRecordingSegment(
    DateTimeOffset Start,
    DateTimeOffset End,
    string? CodecType);

public sealed record IsapiSearchResult(
    IReadOnlyList<IsapiRecordingSegment> Segments,
    bool Truncated);

public interface IIsapiClient
{
    Task<IsapiSearchResult> SearchRecordingsAsync(
        IsapiSearchRequest request,
        CancellationToken cancellationToken);
}
