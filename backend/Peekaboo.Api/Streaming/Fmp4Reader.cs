using System.Buffers.Binary;
using System.Text;

namespace Peekaboo.Api.Streaming;

public sealed record Fmp4Box(string Type, byte[] Data);

/// <summary>
/// Reads complete ISO-BMFF (MP4) boxes from a progressive stream such as ffmpeg's
/// fragmented MP4 output. Each returned box includes its full header.
/// </summary>
public sealed class Fmp4Reader(Stream stream)
{
    private const int MaximumBoxSize = 32 * 1024 * 1024;
    private readonly byte[] _header = new byte[16];

    public async Task<Fmp4Box?> ReadBoxAsync(CancellationToken cancellationToken)
    {
        var headerRead = await ReadAtLeastAsync(_header.AsMemory(0, 8), cancellationToken);
        if (headerRead == 0)
        {
            return null;
        }

        if (headerRead < 8)
        {
            throw new InvalidDataException("Truncated MP4 box header.");
        }

        var size = (ulong)BinaryPrimitives.ReadUInt32BigEndian(_header);
        var type = Encoding.ASCII.GetString(_header, 4, 4);
        var headerSize = 8;

        if (size == 1)
        {
            if (await ReadAtLeastAsync(_header.AsMemory(8, 8), cancellationToken) != 8)
            {
                throw new InvalidDataException($"Truncated MP4 box size for '{type}'.");
            }

            size = BinaryPrimitives.ReadUInt64BigEndian(_header.AsSpan(8));
            headerSize = 16;
        }

        if (size < (ulong)headerSize || size > MaximumBoxSize)
        {
            throw new InvalidDataException($"Invalid MP4 box size {size} for '{type}'.");
        }

        var data = new byte[(int)size];
        _header.AsSpan(0, headerSize).CopyTo(data);

        var payloadLength = (int)size - headerSize;
        if (payloadLength > 0 &&
            await ReadAtLeastAsync(data.AsMemory(headerSize, payloadLength), cancellationToken) != payloadLength)
        {
            throw new InvalidDataException($"Truncated MP4 box payload for '{type}'.");
        }

        return new Fmp4Box(type, data);
    }

    private async Task<int> ReadAtLeastAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
