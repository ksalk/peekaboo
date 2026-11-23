using System.Buffers.Binary;

namespace Peekaboo.Api.Streaming;

public sealed record Fmp4Codecs(string VideoCodec, string? AudioCodec);

public static class Fmp4Codec
{
    private const int MinimumAvcCConfigurationLength = 8;
    private const uint MinimumAvcCBoxSize = 15;

    // An AudioSampleEntry has at least 28 body bytes after the 8-byte box header.
    private const uint MinimumMp4aBoxSize = 36;
    private const uint MinimumHdlrBoxSize = 24;
    private static ReadOnlySpan<byte> Mp4aBoxType => "mp4a"u8;
    private static ReadOnlySpan<byte> SoundHandlerType => "soun"u8;
    private static ReadOnlySpan<byte> HdlrBoxType => "hdlr"u8;

    /// <summary>
    /// Extracts MSE-compatible codec strings from an ffmpeg moov box. The video
    /// codec comes from avcC; the optional audio codec is AAC-LC because that is
    /// what the streaming pipeline asks ffmpeg to produce.
    /// </summary>
    public static Fmp4Codecs? TryGetCodecs(ReadOnlySpan<byte> moovBox)
    {
        var videoCodec = TryGetAvc1Codec(moovBox);
        if (videoCodec is null)
        {
            return null;
        }

        // The pipeline always encodes AAC-LC. If that changes, parse the
        // AudioObjectType from esds instead of hardcoding the profile.
        return new Fmp4Codecs(
            videoCodec,
            HasAacAudioTrack(moovBox) ? "mp4a.40.2" : null);
    }

    /// <summary>
    /// Extracts an MSE-compatible codec string from the avcC box inside an ffmpeg
    /// moov box. Returns null when the stream is not H.264 (for example H.265).
    /// </summary>
    public static string? TryGetAvc1Codec(ReadOnlySpan<byte> moovBox)
    {
        var avcCIndex = moovBox.IndexOf("avcC"u8);
        if (avcCIndex < 4 || avcCIndex + MinimumAvcCConfigurationLength > moovBox.Length)
        {
            return null;
        }

        // Confirm this is a real avcC box rather than a coincidental byte sequence:
        // the size field precedes the type and configurationVersion must be 1.
        var boxStart = avcCIndex - 4;
        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(moovBox[boxStart..]);
        if (boxSize < MinimumAvcCBoxSize || boxSize > (uint)(moovBox.Length - boxStart))
        {
            return null;
        }

        if (moovBox[avcCIndex + 4] != 1)
        {
            return null;
        }

        var profile = moovBox[avcCIndex + 5];
        var compatibility = moovBox[avcCIndex + 6];
        var level = moovBox[avcCIndex + 7];

        if (profile == 0)
        {
            return null;
        }

        return $"avc1.{profile:x2}{compatibility:x2}{level:x2}";
    }

    private static bool HasAacAudioTrack(ReadOnlySpan<byte> moovBox) =>
        ContainsBox(moovBox, Mp4aBoxType, MinimumMp4aBoxSize) &&
        ContainsSoundHandler(moovBox);

    private static bool ContainsBox(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> type,
        uint minimumSize)
    {
        for (var index = 4; index + type.Length <= data.Length; index++)
        {
            if (!data.Slice(index, type.Length).SequenceEqual(type))
            {
                continue;
            }

            var boxStart = index - 4;
            var boxSize = BinaryPrimitives.ReadUInt32BigEndian(data[boxStart..]);
            if (boxSize >= minimumSize && boxSize <= (uint)(data.Length - boxStart))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSoundHandler(ReadOnlySpan<byte> data)
    {
        // hdlr layout: size, type, version/flags, pre_defined, handler_type.
        // The handler_type therefore starts 16 bytes after the box start.
        for (var index = 16; index + SoundHandlerType.Length <= data.Length; index++)
        {
            if (!data.Slice(index, SoundHandlerType.Length).SequenceEqual(SoundHandlerType))
            {
                continue;
            }

            var boxStart = index - 16;
            var boxSize = BinaryPrimitives.ReadUInt32BigEndian(data[boxStart..]);
            if (boxSize < MinimumHdlrBoxSize || boxSize > (uint)(data.Length - boxStart))
            {
                continue;
            }

            if (data.Slice(boxStart + 4, HdlrBoxType.Length).SequenceEqual(HdlrBoxType))
            {
                return true;
            }
        }

        return false;
    }
}
