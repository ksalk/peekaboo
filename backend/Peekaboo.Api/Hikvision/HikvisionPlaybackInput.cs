using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Hikvision;

/// <summary>
/// One recorded range fed into ffmpeg's stdin as an MPEG-PS stream. The SDK
/// delivers data through a callback; a separate pump performs the pipe writes so
/// the callback never blocks the SDK.
/// </summary>
internal sealed class HikvisionPlaybackInput(
    HikvisionPlaybackStreamSource source,
    HikvisionConnectionPool pool,
    ILogger<HikvisionPlaybackInput> logger) : IStreamInput
{
    private const int MaximumPacketBytes = 8 * 1024 * 1024;
    private static readonly ConcurrentDictionary<long, PlaybackCallbackContext> Callbacks = new();
    private static long _nextCallbackId;

    public async Task RunAsync(Stream destination, CancellationToken cancellationToken)
    {
        var key = new HikvisionConnectionKey(source.NvrId, source.Host, source.SdkPort);
        using var lease = await pool.RentAsync(key, cancellationToken);

        var sdkChannel = HikvisionChannelMap.Map(
            lease.Connection.Device,
            source.IsapiChannel);
        logger.LogDebug(
            "Hikvision playback channel mapping for {Source}: ISAPI channel {IsapiChannel} -> SDK channel {SdkChannel}",
            source.SafeDescription,
            source.IsapiChannel,
            sdkChannel);

        var context = new PlaybackCallbackContext();
        var callbackId = Interlocked.Increment(ref _nextCallbackId);
        Callbacks[callbackId] = context;
        var handle = -1;
        Task? pumpTask = null;

        try
        {
            // Start draining before PLAYSTART: some NVRs deliver the whole
            // requested range from inside the start call, which would otherwise
            // fill the callback buffer before the pump is scheduled.
            pumpTask = PumpAsync(context, destination, cancellationToken);

            var para = CreateVodPara(sdkChannel);
            handle = HikvisionNative.NET_DVR_PlayBackByTime_V50(
                lease.Connection.UserId,
                ref para);

            if (handle < 0)
            {
                throw HikvisionErrorMapper.FromLastError(
                    $"start playback on channel {sdkChannel}");
            }

            if (SetPlaybackCallback(handle, callbackId) == 0)
            {
                throw HikvisionErrorMapper.FromLastError("register the playback callback");
            }

            if (SetTranscodeType(handle) == 0)
            {
                logger.LogWarning(
                    "Hikvision playback transcode type for {Source} was rejected; " +
                    "using the device default.",
                    source.SafeDescription);
            }

            if (HikvisionNative.NET_DVR_PlayBackControl_V40(
                    handle,
                    HikvisionConstants.NetDvrPlayStart,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero) == 0)
            {
                throw HikvisionErrorMapper.FromLastError("start playback");
            }

            logger.LogDebug("Hikvision playback started for {Source}", source.SafeDescription);
            await pumpTask;
            logger.LogDebug("Hikvision playback ended for {Source}", source.SafeDescription);
        }
        finally
        {
            Callbacks.TryRemove(callbackId, out _);
            context.Complete();

            if (pumpTask is not null)
            {
                try
                {
                    await pumpTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // The pump reports its own failure through the outer exception.
                }
            }

            context.Drain();

            if (handle >= 0)
            {
                try
                {
                    if (HikvisionNative.NET_DVR_StopPlayBack(handle) == 0)
                    {
                        logger.LogWarning(
                            "Hikvision stop playback for {Source} failed: {Error}",
                            source.SafeDescription,
                            HikvisionNative.LastErrorText());
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Hikvision stop playback for {Source} threw an exception.",
                        source.SafeDescription);
                }
            }
        }
    }

    private NetDvrVodParaV50 CreateVodPara(int sdkChannel) => new()
    {
        dwSize = (uint)Marshal.SizeOf<NetDvrVodParaV50>(),
        struIDInfo = new NetDvrStreamInfo
        {
            dwSize = (uint)Marshal.SizeOf<NetDvrStreamInfo>(),
            byID = new byte[32],
            dwChannel = (uint)sdkChannel,
            byRes = new byte[32],
        },
        struBeginTime = HikvisionTime.ToV50(source.Start),
        struEndTime = HikvisionTime.ToV50(source.End),
        hWnd = 0,
        byDrawFrame = 0,
        byVolumeType = 0,
        byVolumeNum = 0,
        byStreamType = HikvisionConstants.NetDvrStreamTypeMain,
        dwFileIndex = 0,
        byAudioFile = 0,
        byCourseFile = 0,
        byPlayMode = 0,
        byLinkMode = 0,
        byDownload = 0,
        byOptimalStreamType = 0,
        byDisplayBufNum = 0,
        byNPQMode = 0,
        sUserName = new byte[32],
        sPassword = new byte[16],
        byRemoteFile = 0,
        byUseAsyn = 0,
        byRes2 = new byte[201],
        byHls = 0,
        pSavedFileName = IntPtr.Zero,
    };

    private static unsafe int SetPlaybackCallback(int handle, long callbackId) =>
        HikvisionNative.NET_DVR_SetPlayDataCallBack_V40(
            handle,
            &OnPlaybackData,
            (nint)callbackId);

    private static unsafe int SetTranscodeType(int handle)
    {
        var transType = HikvisionConstants.NetDvrTransTypePs;
        return HikvisionNative.NET_DVR_PlayBackControl_V40(
            handle,
            HikvisionConstants.NetDvrSetTransType,
            (IntPtr)(&transType),
            sizeof(int),
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static async Task PumpAsync(
        PlaybackCallbackContext context,
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
    private static unsafe void OnPlaybackData(
        int handle,
        uint dataType,
        byte* data,
        uint size,
        nint user)
    {
        if (!Callbacks.TryGetValue((long)user, out var context))
        {
            return;
        }

        try
        {
            if (dataType == HikvisionConstants.NetDvrPlaybackAllFileEnd)
            {
                context.Complete();
                return;
            }

            if (size == 0 || data is null)
            {
                return;
            }

            context.OnData(dataType, data, size);
        }
        catch (Exception exception)
        {
            context.Fail(exception);
        }
    }

    private sealed class PlaybackCallbackContext
    {
        private const long MaximumQueuedBytes = 64L * 1024 * 1024;
        private readonly Channel<Packet> _packets = Channel.CreateBounded<Packet>(
            new BoundedChannelOptions(4096)
            {
                SingleReader = false,
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
                    "The Hikvision playback stream buffer is full.",
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
                        "The Hikvision playback stream buffer is full.",
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
