using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Hikvision;

/// <summary>
/// One live SDK channel fed into ffmpeg's stdin as an MPEG-PS stream. The SDK
/// callback only copies into a bounded queue; a separate pump performs the pipe
/// writes so the callback never blocks the SDK's delivery thread.
/// </summary>
internal sealed class HikvisionLiveInput(
    HikvisionLiveStreamSource source,
    HikvisionConnectionPool pool,
    ILogger<HikvisionLiveInput> logger) : IStreamInput
{
    private const int MaximumPacketBytes = 8 * 1024 * 1024;
    private static readonly ConcurrentDictionary<long, LiveCallbackContext> Callbacks = new();
    private static long _nextCallbackId;

    public async Task RunAsync(Stream destination, CancellationToken cancellationToken)
    {
        var key = new HikvisionConnectionKey(source.NvrId, source.Host, source.SdkPort);
        using var lease = await pool.RentAsync(key, cancellationToken);

        var sdkChannel = HikvisionChannelMap.Map(
            lease.Connection.Device,
            source.IsapiChannel);
        logger.LogDebug(
            "Hikvision channel mapping for {Source}: ISAPI channel {IsapiChannel} -> SDK channel {SdkChannel}",
            source.SafeDescription,
            source.IsapiChannel,
            sdkChannel);

        var context = new LiveCallbackContext();
        var callbackId = Interlocked.Increment(ref _nextCallbackId);
        Callbacks[callbackId] = context;
        var handle = -1;

        try
        {
            handle = StartRealPlay(
                lease.Connection.UserId,
                sdkChannel,
                callbackId);

            if (handle < 0)
            {
                throw HikvisionErrorMapper.FromLastError(
                    $"start live view on channel {sdkChannel}");
            }

            logger.LogDebug("Hikvision live stream started for {Source}", source.SafeDescription);
            await PumpAsync(context, destination, cancellationToken);
        }
        finally
        {
            // Stop accepting callbacks before stopping the handle. This closes the
            // window where a final callback could write into an already-drained
            // channel, and keeps cleanup unconditional if the native stop throws.
            Callbacks.TryRemove(callbackId, out _);
            context.Complete();
            context.Drain();

            if (handle >= 0)
            {
                try
                {
                    if (HikvisionNative.NET_DVR_StopRealPlay(handle) == 0)
                    {
                        logger.LogWarning(
                            "Hikvision stop live view for {Source} failed: {Error}",
                            source.SafeDescription,
                            HikvisionNative.LastErrorText());
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Hikvision stop live view for {Source} threw an exception.",
                        source.SafeDescription);
                }
            }

            logger.LogDebug("Hikvision live stream stopped for {Source}", source.SafeDescription);
        }
    }

    private unsafe int StartRealPlay(int userId, int sdkChannel, long callbackId)
    {
        var preview = CreatePreview(sdkChannel);
        return HikvisionNative.NET_DVR_RealPlay_V40(
            userId,
            ref preview,
            &OnLiveData,
            (nint)callbackId);
    }

    private NetDvrPreviewInfo CreatePreview(int sdkChannel) => new()
    {
        lChannel = sdkChannel,
        dwStreamType = (uint)source.StreamType,
        dwLinkMode = 0,
        hPlayWnd = 0,
        bBlocked = 1,
        bPassbackRecord = 0,
        byPreviewMode = 0,
        byStreamID = new byte[32],
        byProtoType = 0,
        byRes1 = 0,
        byVideoCodingType = 0,
        dwDisplayBufNum = 1,
        byNPQMode = 0,
        byRecvMetaData = 0,
        byDataType = 0,
        byReconnect = 0,
        byRes = new byte[212],
    };

    private static async Task PumpAsync(
        LiveCallbackContext context,
        Stream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await context.Reader.WaitToReadAsync(cancellationToken))
            {
                while (context.Reader.TryRead(out var packet))
                {
                    try
                    {
                        await destination.WriteAsync(
                            packet.Buffer.AsMemory(0, packet.Length),
                            cancellationToken);
                    }
                    finally
                    {
                        context.Release(packet.Length);
                        ArrayPool<byte>.Shared.Return(packet.Buffer);
                    }
                }
            }

            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            context.Drain();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLiveData(
        int handle,
        uint dataType,
        byte* data,
        uint size,
        nint user)
    {
        if (size == 0 || data is null)
        {
            return;
        }

        if (!Callbacks.TryGetValue((long)user, out var context))
        {
            return;
        }

        try
        {
            context.OnData(dataType, data, size);
        }
        catch (Exception exception)
        {
            context.Fail(exception);
        }
    }

    private sealed class LiveCallbackContext
    {
        private const long MaximumQueuedBytes = 64L * 1024 * 1024;
        private readonly Channel<Packet> _packets = Channel.CreateBounded<Packet>(
            new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });

        private readonly object _gate = new();
        private long _queuedBytes;
        private bool _closed;

        public ChannelReader<Packet> Reader => _packets.Reader;

        public void Release(int length) =>
            Interlocked.Add(ref _queuedBytes, -length);

        public void Drain()
        {
            // Keep this critical section non-blocking: the SDK callback thread
            // shares _gate and must never wait on pipe or network I/O.
            lock (_gate)
            {
                while (_packets.Reader.TryRead(out var packet))
                {
                    Release(packet.Length);
                    ArrayPool<byte>.Shared.Return(packet.Buffer);
                }
            }
        }

        public unsafe void OnData(uint dataType, byte* data, uint size)
        {
            if (dataType is not (
                    HikvisionConstants.NetDvrSysHead or
                    HikvisionConstants.NetDvrStreamData))
            {
                return;
            }

            if (size > MaximumPacketBytes)
            {
                Fail(new HikvisionException(
                    $"The Hikvision SDK delivered an oversized packet ({size} bytes).",
                    HikvisionFailureKind.Device));
                return;
            }

            var queued = Interlocked.Add(ref _queuedBytes, size);
            if (queued > MaximumQueuedBytes)
            {
                Interlocked.Add(ref _queuedBytes, -(long)size);
                Fail(new HikvisionException(
                    "The Hikvision live stream buffer is full.",
                    HikvisionFailureKind.Busy));
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent((int)size);
            new ReadOnlySpan<byte>(data, (int)size).CopyTo(buffer);

            lock (_gate)
            {
                if (_closed)
                {
                    Interlocked.Add(ref _queuedBytes, -(long)size);
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                if (!_packets.Writer.TryWrite(new Packet(buffer, (int)size)))
                {
                    Interlocked.Add(ref _queuedBytes, -(long)size);
                    ArrayPool<byte>.Shared.Return(buffer);
                    _closed = true;
                    _packets.Writer.TryComplete(new HikvisionException(
                        "The Hikvision live stream buffer is full.",
                        HikvisionFailureKind.Busy));
                }
            }
        }

        public void Fail(Exception exception)
        {
            lock (_gate)
            {
                if (!_closed)
                {
                    _closed = true;
                    _packets.Writer.TryComplete(exception);
                }
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (!_closed)
                {
                    _closed = true;
                    _packets.Writer.TryComplete();
                }
            }
        }
    }

    private readonly record struct Packet(byte[] Buffer, int Length);
}
