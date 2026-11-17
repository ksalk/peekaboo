using System.Buffers.Binary;

namespace Peekaboo.Api.Streaming;

public static class Fmp4Codec
{
    private const int MinimumAvcCConfigurationLength = 8;
    private const uint MinimumAvcCBoxSize = 15;

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
}
