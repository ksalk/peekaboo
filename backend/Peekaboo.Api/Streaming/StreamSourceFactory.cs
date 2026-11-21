using Peekaboo.Api.Cameras;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Streaming;

/// <summary>
/// Resolves a camera's stored configuration into an RTSP or Hikvision SDK source.
/// The decision lives here so endpoints and the stream manager never build URLs or
/// map SDK channels themselves.
/// </summary>
public sealed class StreamSourceFactory
{
    public StreamSource? CreateLive(CameraStreamTarget target)
    {
        if (target.NvrTransport == NvrTransports.Rtsp)
        {
            return new RtspStreamSource(target.Url);
        }

        if (target.NvrTransport == NvrTransports.HikvisionSdk)
        {
            if (!HikvisionChannelMap.TryParseStreamId(
                    target.StreamId,
                    out var isapiChannel,
                    out var streamType))
            {
                throw new HikvisionException(
                    $"Stream ID '{target.StreamId}' is not a valid SDK live stream ID. " +
                    "Use channel * 100 + 1 (main) or 2 (sub), for example 101 or 102.",
                    HikvisionFailureKind.Channel);
            }

            return new HikvisionLiveStreamSource(
                target.NvrId,
                target.NvrHost,
                target.SdkPort,
                isapiChannel,
                streamType);
        }

        throw new HikvisionException(
            $"NVR transport '{target.NvrTransport}' is not supported.",
            HikvisionFailureKind.Unavailable);
    }
}
