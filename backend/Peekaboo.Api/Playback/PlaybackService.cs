using Peekaboo.Api.Cameras;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Playback;

public sealed class PlaybackService
{
    private readonly CameraService _cameras;
    private readonly IIsapiClient _isapi;
    private readonly IHikvisionRecordingSearch _hikvisionSearch;
    private readonly INvrClockResolver _clocks;

    internal PlaybackService(
        CameraService cameras,
        IIsapiClient isapi,
        IHikvisionRecordingSearch hikvisionSearch,
        INvrClockResolver clocks)
    {
        _cameras = cameras;
        _isapi = isapi;
        _hikvisionSearch = hikvisionSearch;
        _clocks = clocks;
    }

    public async Task<INvrTimeZone?> GetTimeZoneAsync(
        string cameraId,
        CancellationToken cancellationToken)
    {
        var source = _cameras.GetPlaybackSource(cameraId);
        if (source is null)
        {
            return null;
        }

        var clock = await _clocks.ResolveAsync(
            source.NvrId,
            source.RtspBaseUrl,
            source.NvrTimeZone,
            cancellationToken);
        return clock.TimeZone;
    }

    public StreamSource? GetStreamSource(
        string cameraId,
        DateTimeOffset start,
        DateTimeOffset end,
        INvrTimeZone timeZone)
    {
        var source = _cameras.GetPlaybackSource(cameraId);
        if (source is null)
        {
            return null;
        }

        if (source.NvrTransport == NvrTransports.Rtsp)
        {
            return new RtspStreamSource(
                IsapiPlaybackUrl.Build(
                    source.RtspBaseUrl,
                    source.MainStreamId,
                    start,
                    end,
                    timeZone),
                Playback: true,
                Duration: end - start);
        }

        if (source.NvrTransport == NvrTransports.HikvisionSdk)
        {
            if (!HikvisionChannelMap.TryParseStreamId(
                    source.MainStreamId,
                    out var isapiChannel,
                    out _))
            {
                throw new HikvisionException(
                    $"Stream ID '{source.MainStreamId}' is not a valid SDK playback stream ID. " +
                    "Use the camera's main stream ID, for example 101.",
                    HikvisionFailureKind.Channel);
            }

            return new HikvisionPlaybackStreamSource(
                source.NvrId,
                source.NvrHost,
                source.SdkPort,
                isapiChannel,
                start,
                end);
        }

        throw new InvalidOperationException(
            $"NVR transport '{source.NvrTransport}' is not supported.");
    }

    public async Task<RecordingSearchDto?> SearchAsync(
        string cameraId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var source = _cameras.GetPlaybackSource(cameraId);
        if (source is null)
        {
            return null;
        }

        var clock = await _clocks.ResolveAsync(
            source.NvrId,
            source.RtspBaseUrl,
            source.NvrTimeZone,
            cancellationToken);

        if (source.NvrTransport == NvrTransports.HikvisionSdk)
        {
            var sdkResult = await _hikvisionSearch.SearchAsync(
                new HikvisionSearchRequest(
                    source.NvrId,
                    source.NvrHost,
                    source.SdkPort,
                    source.MainStreamId,
                    start,
                    end),
                cancellationToken);

            return new RecordingSearchDto(
                source.CameraId,
                source.CameraName,
                start,
                end,
                sdkResult.Truncated,
                sdkResult.Segments.Select(ToSdkRecording).ToList());
        }

        if (source.NvrTransport != NvrTransports.Rtsp)
        {
            throw new InvalidOperationException(
                $"NVR transport '{source.NvrTransport}' is not supported.");
        }

        var result = await _isapi.SearchRecordingsAsync(
            new IsapiSearchRequest(
                source.NvrId,
                source.RtspBaseUrl,
                source.MainStreamId,
                start,
                end,
                clock.TimeZone),
            cancellationToken);

        var recordings = result.Segments
            .Where(segment => segment.End > segment.Start)
            .Select(segment =>
            {
                var durationSeconds = (int)Math.Round(
                    (segment.End - segment.Start).TotalSeconds,
                    MidpointRounding.AwayFromZero);

                return new RecordingSegmentDto(
                    segment.Start,
                    segment.End,
                    Math.Max(0, durationSeconds),
                    segment.CodecType,
                    IsapiPlaybackUrl.Build(
                        source.RtspBaseUrl,
                        source.MainStreamId,
                        segment.Start,
                        segment.End,
                        clock.TimeZone));
            })
            .ToList();

        return new RecordingSearchDto(
            source.CameraId,
            source.CameraName,
            start,
            end,
            result.Truncated,
            recordings);
    }

    private static RecordingSegmentDto ToSdkRecording(HikvisionRecordingSegment segment)
    {
        var durationSeconds = (int)Math.Round(
            (segment.End - segment.Start).TotalSeconds,
            MidpointRounding.AwayFromZero);

        return new RecordingSegmentDto(
            segment.Start,
            segment.End,
            Math.Max(0, durationSeconds),
            CodecType: null,
            PlaybackUrl: string.Empty);
    }
}
