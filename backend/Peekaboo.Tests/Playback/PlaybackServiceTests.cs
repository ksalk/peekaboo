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
    private readonly PlaybackService _playbacks;

    public PlaybackServiceTests()
    {
        _database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));
        _database.Initialize(NullLogger<AppDatabase>.Instance);
        _nvrs = new NvrService(_database);
        _cameras = new CameraService(_database);
        _playbacks = new PlaybackService(_cameras, new EmptyIsapiClient());
    }

    [Fact]
    public void Rtsp_playback_keeps_the_isapi_track_url()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.10.60:554");
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");
        Assert.NotNull(camera);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var source = Assert.IsType<RtspStreamSource>(
            _playbacks.GetStreamSource(camera.Id, start, start.AddMinutes(5)));

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
            _playbacks.GetStreamSource(camera.Id, start, end));

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
            () => _playbacks.GetStreamSource(camera.Id, start, start.AddMinutes(5)));

        Assert.Equal(HikvisionFailureKind.Channel, exception.Kind);
    }

    private sealed class EmptyIsapiClient : IIsapiClient
    {
        public Task<IsapiSearchResult> SearchRecordingsAsync(
            IsapiSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new IsapiSearchResult([], Truncated: false));
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
