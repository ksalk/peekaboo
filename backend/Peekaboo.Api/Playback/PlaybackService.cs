using Peekaboo.Api.Cameras;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Playback;

public sealed class PlaybackService(CameraService cameras, IIsapiClient isapi)
{
    public StreamSource? GetStreamSource(
        string cameraId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var source = cameras.GetPlaybackSource(cameraId);
        if (source is null)
        {
            return null;
        }

        return new RtspStreamSource(
            IsapiPlaybackUrl.Build(
                source.RtspBaseUrl,
                source.MainStreamId,
                start,
                end),
            Playback: true,
            Duration: end - start);
    }

    public async Task<RecordingSearchDto?> SearchAsync(
        string cameraId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var source = cameras.GetPlaybackSource(cameraId);
        if (source is null)
        {
            return null;
        }

        var result = await isapi.SearchRecordingsAsync(
            new IsapiSearchRequest(
                source.NvrId,
                source.RtspBaseUrl,
                source.MainStreamId,
                start,
                end),
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
                        segment.End));
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
}
