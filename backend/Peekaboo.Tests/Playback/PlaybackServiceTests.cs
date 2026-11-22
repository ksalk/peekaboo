using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Data;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Peekaboo.Api.Playback;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Playback;

public sealed class PlaybackServiceTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-playback-service-tests-").FullName;

    private readonly AppDatabase _database;
    private readonly NvrService _nvrs;
    private readonly CameraService _cameras;
    private readonly FakeIsapiClient _isapi = new();
    private readonly FakeHikvisionSearch _sdkSearch = new();
    private readonly FakeClockResolver _clocks = new();
    private readonly PlaybackService _playbacks;

    public PlaybackServiceTests()
    {
        _database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));
        _database.Initialize(NullLogger<AppDatabase>.Instance);
        _nvrs = new NvrService(_database);
        _cameras = new CameraService(_database);
        _playbacks = new PlaybackService(_cameras, _isapi, _sdkSearch, _clocks);
    }

    [Fact]
    public void Rtsp_playback_keeps_the_isapi_track_url()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.10.60:554");
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");
        Assert.NotNull(camera);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var source = Assert.IsType<RtspStreamSource>(
            _playbacks.GetStreamSource(
                camera.Id,
                start,
                start.AddMinutes(5),
                _clocks.TimeZone));

        Assert.True(source.IsPlayback);
        Assert.Equal(TimeSpan.FromMinutes(5), source.MaximumDuration);
        Assert.Contains("starttime=20261120T100000Z", source.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Sdk_playback_uses_the_main_stream_channel_and_utc_range()
    {
        var nvr = _nvrs.Create(
            "SDK NVR",
            "rtsp://192.168.10.60:554",
            NvrTransports.HikvisionSdk,
            8000);
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");
        Assert.NotNull(camera);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(5);
        var source = Assert.IsType<HikvisionPlaybackStreamSource>(
            _playbacks.GetStreamSource(camera.Id, start, end, _clocks.TimeZone));

        Assert.True(source.IsPlayback);
        Assert.Equal(TimeSpan.FromMinutes(5), source.MaximumDuration);
        Assert.Equal(nvr.Id, source.NvrId);
        Assert.Equal("192.168.10.60", source.Host);
        Assert.Equal(8000, source.SdkPort);
        Assert.Equal(1, source.IsapiChannel);
        Assert.Equal(start, source.Start);
        Assert.Equal(end, source.End);
    }

    [Fact]
    public void Sdk_playback_rejects_an_invalid_main_stream_id()
    {
        var nvr = _nvrs.Create(
            "SDK NVR",
            "rtsp://192.168.10.60:554",
            NvrTransports.HikvisionSdk,
            8000);
        var camera = _cameras.Create(nvr.Id, "Front door", "100", "102");
        Assert.NotNull(camera);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var exception = Assert.Throws<HikvisionException>(
            () => _playbacks.GetStreamSource(
                camera.Id,
                start,
                start.AddMinutes(5),
                _clocks.TimeZone));

        Assert.Equal(HikvisionFailureKind.Channel, exception.Kind);
    }

    [Fact]
    public async Task Sdk_search_uses_the_sdk_client_and_returns_no_playback_url()
    {
        var nvr = _nvrs.Create(
            "SDK NVR",
            "rtsp://192.168.10.60:554",
            NvrTransports.HikvisionSdk,
            8000);
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");
        Assert.NotNull(camera);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(5);
        var segmentStart = start.AddMinutes(1);
        var segmentEnd = segmentStart.AddSeconds(30);
        _sdkSearch.Result = new HikvisionSearchResult(
            [new HikvisionRecordingSegment(segmentStart, segmentEnd, "file.mp4")],
            Truncated: false);

        var result = await _playbacks.SearchAsync(camera.Id, start, end, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.Truncated);
        var recording = Assert.Single(result.Recordings);
        Assert.Equal(segmentStart, recording.Start);
        Assert.Equal(segmentEnd, recording.End);
        Assert.Equal(30, recording.DurationSeconds);
        Assert.Null(recording.CodecType);
        Assert.Equal(string.Empty, recording.PlaybackUrl);
        Assert.Equal(0, _isapi.CallCount);

        Assert.NotNull(_sdkSearch.LastRequest);
        Assert.Equal(nvr.Id, _sdkSearch.LastRequest.NvrId);
        Assert.Equal("192.168.10.60", _sdkSearch.LastRequest.Host);
        Assert.Equal(8000, _sdkSearch.LastRequest.SdkPort);
        Assert.Equal("101", _sdkSearch.LastRequest.MainStreamId);
        Assert.Equal(start, _sdkSearch.LastRequest.Start);
        Assert.Equal(end, _sdkSearch.LastRequest.End);
    }

    [Fact]
    public async Task Rtsp_search_uses_isapi_and_keeps_the_playback_url()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.10.60:554");
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");
        Assert.NotNull(camera);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(5);
        var segmentStart = start.AddMinutes(1);
        var segmentEnd = segmentStart.AddSeconds(30);
        _isapi.Result = new IsapiSearchResult(
            [new IsapiRecordingSegment(segmentStart, segmentEnd, "H.264")],
            Truncated: false);

        var result = await _playbacks.SearchAsync(camera.Id, start, end, CancellationToken.None);

        Assert.NotNull(result);
        var recording = Assert.Single(result.Recordings);
        Assert.Equal(segmentStart, recording.Start);
        Assert.Equal(segmentEnd, recording.End);
        Assert.Equal(30, recording.DurationSeconds);
        Assert.Equal("H.264", recording.CodecType);
        Assert.Contains("/ISAPI/Streaming/tracks/101", recording.PlaybackUrl, StringComparison.Ordinal);
        Assert.Equal(1, _isapi.CallCount);
        Assert.NotNull(_isapi.LastRequest);
        Assert.Same(_clocks.TimeZone, _isapi.LastRequest.TimeZone);
        Assert.Null(_sdkSearch.LastRequest);
    }

    [Fact]
    public void Rtsp_playback_url_uses_the_nvr_wall_clock()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.10.60:554");
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");
        Assert.NotNull(camera);

        _clocks.TimeZone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        var source = Assert.IsType<RtspStreamSource>(
            _playbacks.GetStreamSource(
                camera.Id,
                start,
                start.AddMinutes(5),
                _clocks.TimeZone));

        Assert.Contains("starttime=20261120T110000Z", source.Url, StringComparison.Ordinal);
        Assert.Contains("endtime=20261120T110500Z", source.Url, StringComparison.Ordinal);
    }

    private sealed class FakeClockResolver : INvrClockResolver
    {
        public INvrTimeZone TimeZone { get; set; } = TestNvrTimeZones.Utc;

        public Task<NvrClock> ResolveAsync(
            string nvrId,
            string baseUrl,
            string timeZone,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NvrClock(
                TimeZone,
                "test",
                DetectedTimeZone: null,
                DateTimeOffset.UtcNow,
                Error: null));

        public void Invalidate(string nvrId)
        {
        }
    }

    private sealed class FakeHikvisionSearch : IHikvisionRecordingSearch
    {
        public HikvisionSearchRequest? LastRequest { get; private set; }

        public HikvisionSearchResult Result { get; set; } = new([], Truncated: false);

        public Task<HikvisionSearchResult> SearchAsync(
            HikvisionSearchRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeIsapiClient : IIsapiClient
    {
        public int CallCount { get; private set; }

        public IsapiSearchRequest? LastRequest { get; private set; }

        public IsapiSearchResult Result { get; set; } = new([], Truncated: false);

        public Task<IsapiSearchResult> SearchRecordingsAsync(
            IsapiSearchRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
