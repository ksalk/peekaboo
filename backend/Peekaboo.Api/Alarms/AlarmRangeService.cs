using Microsoft.Extensions.Caching.Memory;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Alarms;

public sealed record AlarmRangeDto(
    string CameraId,
    string CameraName,
    string NvrId,
    string NvrName,
    int ChannelId,
    DateTimeOffset Start,
    DateTimeOffset End,
    int DurationSeconds,
    string Target,
    string? TargetLabel,
    bool InferredStart,
    bool InferredEnd);

public sealed record AlarmSearchErrorDto(
    string NvrId,
    string NvrName,
    string Message);

public sealed record AlarmSearchDto(
    DateTimeOffset Start,
    DateTimeOffset End,
    bool Truncated,
    IReadOnlyList<AlarmRangeDto> Ranges,
    IReadOnlyList<AlarmSearchErrorDto> Errors);

public sealed class AlarmRangeService
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly CameraService _cameras;
    private readonly NvrService _nvrs;
    private readonly IIsapiAlarmLogClient _logs;
    private readonly INvrClockResolver _clocks;
    private readonly IMemoryCache _cache;

    public AlarmRangeService(
        CameraService cameras,
        NvrService nvrs,
        IIsapiAlarmLogClient logs,
        INvrClockResolver clocks,
        IMemoryCache cache)
    {
        _cameras = cameras;
        _nvrs = nvrs;
        _logs = logs;
        _clocks = clocks;
        _cache = cache;
    }

    public async Task<AlarmSearchDto?> SearchAsync(
        string? cameraId,
        string? nvrId,
        DateTimeOffset start,
        DateTimeOffset end,
        AlarmTarget? target,
        CancellationToken cancellationToken)
    {
        var allCameras = _cameras.List();
        var allNvrs = _nvrs.List();
        var queries = new List<(NvrDto Nvr, IReadOnlyList<CameraDto> Cameras)>();

        if (cameraId is not null)
        {
            var camera = allCameras.FirstOrDefault(
                entry => string.Equals(entry.Id, cameraId, StringComparison.Ordinal));
            if (camera is null)
            {
                return null;
            }

            var nvr = allNvrs.FirstOrDefault(
                entry => string.Equals(entry.Id, camera.NvrId, StringComparison.Ordinal));
            if (nvr is null)
            {
                return null;
            }

            queries.Add((nvr, [camera]));
        }
        else if (nvrId is not null)
        {
            var nvr = allNvrs.FirstOrDefault(
                entry => string.Equals(entry.Id, nvrId, StringComparison.Ordinal));
            if (nvr is null)
            {
                return null;
            }

            queries.Add((nvr, allCameras
                .Where(camera => string.Equals(
                    camera.NvrId,
                    nvr.Id,
                    StringComparison.Ordinal))
                .ToList()));
        }
        else
        {
            foreach (var nvr in allNvrs)
            {
                var cameras = allCameras
                    .Where(camera => string.Equals(
                        camera.NvrId,
                        nvr.Id,
                        StringComparison.Ordinal))
                    .ToList();

                if (cameras.Count > 0)
                {
                    queries.Add((nvr, cameras));
                }
            }
        }

        var ranges = new List<AlarmRangeDto>();
        var errors = new List<AlarmSearchErrorDto>();
        var truncated = false;

        foreach (var (nvr, cameras) in queries)
        {
            var byChannel = CameraByChannel(cameras);
            if (byChannel.Count == 0)
            {
                continue;
            }

            var queryStart = start - Margin;
            var queryEnd = end + Margin;

            try
            {
                var clock = await _clocks.ResolveAsync(
                    nvr.Id,
                    nvr.BaseUrl,
                    nvr.TimeZone,
                    cancellationToken);

                var logs = await SearchLogsAsync(
                    nvr,
                    queryStart,
                    queryEnd,
                    clock.TimeZone,
                    cancellationToken);
                truncated |= logs.Truncated;

                foreach (var range in MotionRangeBuilder.Build(
                             logs.Entries,
                             queryStart,
                             queryEnd))
                {
                    if (!byChannel.TryGetValue(range.ChannelId, out var camera))
                    {
                        continue;
                    }

                    if (target is { } filter && range.Target != filter)
                    {
                        continue;
                    }

                    var clippedStart = range.Start < start ? start : range.Start;
                    var clippedEnd = range.End > end ? end : range.End;
                    if (clippedEnd <= clippedStart)
                    {
                        continue;
                    }

                    var durationSeconds = (int)Math.Round(
                        (clippedEnd - clippedStart).TotalSeconds,
                        MidpointRounding.AwayFromZero);

                    ranges.Add(new AlarmRangeDto(
                        camera.Id,
                        camera.Name,
                        nvr.Id,
                        nvr.Name,
                        range.ChannelId,
                        clippedStart,
                        clippedEnd,
                        Math.Max(0, durationSeconds),
                        AlarmTargets.ToStorageValue(range.Target),
                        range.TargetLabel,
                        range.InferredStart,
                        range.InferredEnd));
                }
            }
            catch (IsapiException exception) when (queries.Count > 1)
            {
                // One offline NVR must not hide alarms from the healthy ones.
                errors.Add(new AlarmSearchErrorDto(
                    nvr.Id,
                    nvr.Name,
                    DescribeFailure(exception)));
            }
        }

        ranges.Sort((left, right) =>
        {
            var byStart = right.Start.CompareTo(left.Start);
            return byStart != 0
                ? byStart
                : string.Compare(left.CameraName, right.CameraName, StringComparison.OrdinalIgnoreCase);
        });

        return new AlarmSearchDto(start, end, truncated, ranges, errors);
    }

    private static string DescribeFailure(IsapiException exception) =>
        exception.Kind switch
        {
            IsapiFailureKind.Unauthorized =>
                "The NVR rejected the log-search credentials or permissions.",
            IsapiFailureKind.NotFound =>
                "The NVR does not expose the ISAPI log-search endpoint.",
            IsapiFailureKind.Timeout =>
                "The NVR did not answer the log search in time.",
            IsapiFailureKind.Unreachable =>
                "The NVR ISAPI service is unreachable.",
            IsapiFailureKind.InvalidResponse =>
                "The NVR returned an invalid log response.",
            _ => "The NVR reported an error while searching its log.",
        };

    private async Task<IsapiAlarmLogResult> SearchLogsAsync(
        NvrDto nvr,
        DateTimeOffset start,
        DateTimeOffset end,
        INvrTimeZone timeZone,
        CancellationToken cancellationToken)
    {
        var key = $"alarm-log:{nvr.Id}:{timeZone.Id}:{start.UtcTicks}:{end.UtcTicks}";
        if (_cache.TryGetValue(key, out IsapiAlarmLogResult? cached) && cached is not null)
        {
            return cached;
        }

        var result = await _logs.SearchAsync(
            new IsapiAlarmLogRequest(
                nvr.Id,
                nvr.BaseUrl,
                start,
                end,
                timeZone),
            cancellationToken);

        _cache.Set(
            key,
            result,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheLifetime,
                // Approximate memory pressure by the number of parsed entries.
                Size = CacheSize(result),
            });
        return result;
    }

    internal static int CacheSize(IsapiAlarmLogResult result) =>
        Math.Max(1, result.Entries.Count);

    private static Dictionary<int, CameraDto> CameraByChannel(
        IReadOnlyList<CameraDto> cameras)
    {
        var byChannel = new Dictionary<int, CameraDto>();

        foreach (var camera in cameras)
        {
            if (HikvisionChannelMap.TryParseStreamId(
                    camera.MainStreamId,
                    out var channel,
                    out _))
            {
                byChannel.TryAdd(channel, camera);
            }
        }

        return byChannel;
    }
}
