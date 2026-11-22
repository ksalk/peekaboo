using System.Runtime.InteropServices;
using System.Text;

using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Hikvision;

internal sealed record HikvisionSearchRequest(
    string NvrId,
    string Host,
    int SdkPort,
    string MainStreamId,
    DateTimeOffset Start,
    DateTimeOffset End,
    INvrTimeZone TimeZone);

internal sealed record HikvisionRecordingSegment(
    DateTimeOffset Start,
    DateTimeOffset End,
    string FileName);

internal sealed record HikvisionSearchResult(
    IReadOnlyList<HikvisionRecordingSegment> Segments,
    bool Truncated);

internal interface IHikvisionRecordingSearch
{
    Task<HikvisionSearchResult> SearchAsync(
        HikvisionSearchRequest request,
        CancellationToken cancellationToken);
}

internal sealed class HikvisionRecordingSearch(
    HikvisionConnectionPool pool,
    ILogger<HikvisionRecordingSearch> logger) : IHikvisionRecordingSearch
{
    private const int MaximumResults = 1000;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(60);

    public async Task<HikvisionSearchResult> SearchAsync(
        HikvisionSearchRequest request,
        CancellationToken cancellationToken)
    {
        var key = new HikvisionConnectionKey(
            request.NvrId,
            request.Host,
            request.SdkPort);
        using var lease = await pool.RentAsync(key, cancellationToken);

        if (!HikvisionChannelMap.TryParseStreamId(
                request.MainStreamId,
                out var isapiChannel,
                out _))
        {
            throw new HikvisionException(
                $"Stream ID '{request.MainStreamId}' is not a valid SDK search stream ID. " +
                "Use the camera's main stream ID, for example 101.",
                HikvisionFailureKind.Channel);
        }

        var sdkChannel = HikvisionChannelMap.Map(
            lease.Connection.Device,
            isapiChannel);
        var offset = request.TimeZone.GetUtcOffset(request.Start.UtcDateTime);
        if (offset != TimeSpan.Zero)
        {
            logger.LogDebug(
                "SDK recording search for NVR {NvrId} uses device-local fields " +
                "with offset {Offset}",
                request.NvrId,
                offset);
        }

        var condition = CreateCondition(
            sdkChannel,
            request.Start,
            request.End,
            request.TimeZone);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SearchTimeout);

        // FindFile can block on the NVR; keep it off the request thread.
        int findHandle;
        try
        {
            findHandle = await Task.Run(
                () => HikvisionNative.NET_DVR_FindFile_V50(
                    lease.Connection.UserId,
                    ref condition),
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HikvisionException(
                "The NVR did not finish the SDK recording search in time.",
                HikvisionFailureKind.Timeout);
        }

        if (findHandle < 0)
        {
            throw HikvisionErrorMapper.FromLastError("search recordings");
        }

        var segments = new List<HikvisionRecordingSegment>();
        var seen = new HashSet<(long Start, long End, string FileName)>();
        var truncated = false;

        try
        {
            var data = CreateFindData();
            var warnedAboutLocalTimes = false;

            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();

                var result = HikvisionNative.NET_DVR_FindNextFile_V50(
                    findHandle,
                    ref data);

                if (result == HikvisionConstants.NetDvrIsFinding)
                {
                    await Task.Delay(50, timeout.Token);
                    continue;
                }

                if (result is HikvisionConstants.NetDvrFileNoFind or
                    HikvisionConstants.NetDvrNoMoreFile)
                {
                    break;
                }

                if (result != HikvisionConstants.NetDvrFileSuccess)
                {
                    throw HikvisionErrorMapper.FromLastError("read a recording search result");
                }

                if (!warnedAboutLocalTimes &&
                    (data.struStartTime.byLocalOrUTC == 0 ||
                     data.struStopTime.byLocalOrUTC == 0))
                {
                    warnedAboutLocalTimes = true;
                    logger.LogWarning(
                        "The NVR returned a recording time without a UTC offset for NVR {NvrId}; " +
                        "applying the resolved device time zone.",
                        request.NvrId);
                }

                DateTimeOffset start;
                DateTimeOffset end;

                try
                {
                    start = HikvisionTime.FromSearch(
                        data.struStartTime,
                        request.TimeZone);
                    end = HikvisionTime.FromSearch(
                        data.struStopTime,
                        request.TimeZone);
                }
                catch (HikvisionException exception) when (
                    exception.Kind == HikvisionFailureKind.Device)
                {
                    logger.LogDebug(
                        exception,
                        "Skipping a recording search entry with an invalid timestamp.");
                    continue;
                }

                var fileName = DecodeFileName(data.sFileName);
                if (end > start &&
                    seen.Add((start.UtcTicks, end.UtcTicks, fileName)))
                {
                    segments.Add(new HikvisionRecordingSegment(start, end, fileName));
                }

                if (segments.Count >= MaximumResults)
                {
                    // Consume one more entry to distinguish exactly 1000 results
                    // from a genuinely truncated result set.
                    var peek = HikvisionNative.NET_DVR_FindNextFile_V50(
                        findHandle,
                        ref data);

                    while (peek == HikvisionConstants.NetDvrIsFinding)
                    {
                        await Task.Delay(50, timeout.Token);
                        peek = HikvisionNative.NET_DVR_FindNextFile_V50(
                            findHandle,
                            ref data);
                    }

                    if (peek == HikvisionConstants.NetDvrFileSuccess)
                    {
                        truncated = true;
                    }
                    else if (peek is not (
                                 HikvisionConstants.NetDvrFileNoFind or
                                 HikvisionConstants.NetDvrNoMoreFile))
                    {
                        logger.LogWarning(
                            "The NVR returned an unexpected result while checking for " +
                            "additional recording search entries: {Error}",
                            HikvisionNative.LastErrorText());
                        truncated = true;
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HikvisionException(
                "The NVR did not finish the SDK recording search in time.",
                HikvisionFailureKind.Timeout);
        }
        finally
        {
            if (HikvisionNative.NET_DVR_FindClose_V30(findHandle) == 0)
            {
                logger.LogWarning(
                    "Hikvision recording search close failed: {Error}",
                    HikvisionNative.LastErrorText());
            }
        }

        segments.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        logger.LogDebug(
            "SDK recording search for NVR {NvrId} channel {Channel} returned {Count} segments (truncated: {Truncated})",
            request.NvrId,
            sdkChannel,
            segments.Count,
            truncated);

        return new HikvisionSearchResult(segments, truncated);
    }

    private static NetDvrFileCondV50 CreateCondition(
        int sdkChannel,
        DateTimeOffset start,
        DateTimeOffset end,
        INvrTimeZone timeZone) =>
        new()
        {
            struStreamID = new NetDvrStreamInfo
            {
                dwSize = (uint)Marshal.SizeOf<NetDvrStreamInfo>(),
                byID = new byte[32],
                dwChannel = (uint)sdkChannel,
                byRes = new byte[32],
            },
            struStartTime = HikvisionTime.ToSearchCond(start, timeZone),
            struStopTime = HikvisionTime.ToSearchCond(end, timeZone),
            byFindType = 0,
            byDrawFrame = 0,
            byQuickSearch = 0,
            byStreamType = HikvisionConstants.NetDvrStreamTypeMain,
            dwFileType = 0xff,
            dwVolumeNum = 0,
            byIsLocked = 0xff,
            byNeedCard = 0,
            byOnlyAudioFile = 0,
            bySpecialFindInfoType = 0,
            szCardNum = new byte[32],
            szWorkingDeviceGUID = new byte[16],
            uSpecialFindInfo = new byte[8],
            dwTimeout = 0,
            byRes = new byte[252],
        };

    private static NetDvrFindDataV50 CreateFindData() =>
        new()
        {
            sFileName = new byte[100],
            struAddr = new byte[148],
            sCardNum = new byte[32],
            byRes = new byte[247],
        };

    private static string DecodeFileName(byte[] fileName) =>
        Encoding.ASCII.GetString(fileName).TrimEnd('\0', ' ');
}
