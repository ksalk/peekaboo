namespace Peekaboo.Api.Playback;

public sealed record RecordingSegmentDto(
    DateTimeOffset Start,
    DateTimeOffset End,
    int DurationSeconds,
    string? CodecType,
    string PlaybackUrl);

public sealed record RecordingSearchDto(
    string CameraId,
    string CameraName,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool Truncated,
    IReadOnlyList<RecordingSegmentDto> Recordings);
