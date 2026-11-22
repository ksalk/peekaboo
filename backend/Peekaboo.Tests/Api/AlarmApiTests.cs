using System.Net;
using System.Net.Http.Json;
using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class AlarmApiTests
{
    [Fact]
    public async Task Alarm_search_requires_authentication()
    {
        using var host = new ApiTestHost(alarmLogClient: new FakeAlarmLogClient());
        using var client = host.CreateClient();

        using var response = await client.GetAsync(
            "/api/alarms?start=2026-11-21T00:00:00Z&end=2026-11-22T00:00:00Z");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Alarm_search_maps_ranges_and_expands_the_nvr_query_window()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [
                    Start(1, 9, 59, 30, "Osoba"),
                    Stop(1, 10, 0, 30, "Osoba"),
                    Start(1, 10, 10, 0, "Pojazd"),
                    Stop(1, 10, 10, 10, "Pojazd"),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        Assert.False(result.Truncated);
        Assert.Equal(2, result.Ranges.Count);
        Assert.Equal("vehicle", result.Ranges[0].Target);
        Assert.Equal("human", result.Ranges[1].Target);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero),
            result.Ranges[1].Start);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 10, 0, 30, TimeSpan.Zero),
            result.Ranges[1].End);
        Assert.Equal("Front door", result.Ranges[1].CameraName);

        var request = Assert.Single(fake.Requests);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 9, 55, 0, TimeSpan.Zero),
            request.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 11, 5, 0, TimeSpan.Zero),
            request.End);
    }

    [Fact]
    public async Task Alarm_search_filters_by_target()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [
                    Start(1, 10, 0, 0, "Osoba"),
                    Stop(1, 10, 0, 7, "Osoba"),
                    Start(1, 10, 10, 0, "Pojazd"),
                    Stop(1, 10, 10, 7, "Pojazd"),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z&target=human");

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        Assert.Equal("human", Assert.Single(result.Ranges).Target);
    }

    [Fact]
    public async Task Alarm_search_maps_all_cameras_from_one_nvr_query()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [
                    Start(1, 10, 0, 0, "Osoba"),
                    Stop(1, 10, 0, 7, "Osoba"),
                    Start(2, 10, 5, 0, "Pojazd"),
                    Stop(2, 10, 5, 7, "Pojazd"),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, _, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            "/api/alarms?start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Ranges.Count);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Alarm_search_reports_truncated_and_inferred_ranges()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [Start(1, 10, 20, 0, "Osoba")],
                Truncated: true),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        Assert.True(result.Truncated);
        var range = Assert.Single(result.Ranges);
        Assert.False(range.InferredStart);
        Assert.True(range.InferredEnd);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero),
            range.End);
    }

    [Fact]
    public async Task Alarm_search_filters_unclassified_targets()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [
                    Start(1, 10, 0, 0, "Osoba"),
                    Stop(1, 10, 0, 7, "Osoba"),
                    Start(1, 10, 10, 0, null),
                    Stop(1, 10, 10, 7, null),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z&target=unknown");

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        Assert.Equal("unknown", Assert.Single(result.Ranges).Target);
    }

    [Fact]
    public async Task Alarm_search_keeps_a_stop_that_started_before_the_query_window()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [Stop(1, 10, 0, 30, "Osoba")],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        var range = Assert.Single(result.Ranges);
        Assert.True(range.InferredStart);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero),
            range.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 10, 0, 30, TimeSpan.Zero),
            range.End);
    }

    [Fact]
    public async Task Alarm_search_caches_repeated_nvr_queries()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [
                    Start(1, 10, 0, 0, "Osoba"),
                    Stop(1, 10, 0, 7, "Osoba"),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        var path = $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z";

        using var first = await client.GetAsync(path);
        using var second = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Alarm_search_does_not_reuse_the_cache_for_a_different_window()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult([], Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var first = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");
        using var second = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T12:00:00Z");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Theory]
    [InlineData("", "", "required")]
    [InlineData(
        "2026-11-21T10:00:00Z",
        "2026-11-21T10:00:00Z",
        "after")]
    [InlineData(
        "2026-10-01T00:00:00Z",
        "2026-12-10T00:00:00Z",
        "31")]
    public async Task Alarm_search_rejects_invalid_ranges(
        string start,
        string end,
        string expectedErrorFragment)
    {
        var fake = new FakeAlarmLogClient();
        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            $"&start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedErrorFragment, body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Alarm_search_rejects_an_invalid_target()
    {
        var fake = new FakeAlarmLogClient();
        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z&target=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Alarm_search_returns_not_found_for_missing_resources()
    {
        var fake = new FakeAlarmLogClient();
        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, _, nvrId) = await SetupAsync(host);
        using var _ = client;

        using var missingCamera = await client.GetAsync(
            "/api/alarms?cameraId=missing" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");
        Assert.Equal(HttpStatusCode.NotFound, missingCamera.StatusCode);

        using var missingNvr = await client.GetAsync(
            "/api/alarms?nvrId=missing" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");
        Assert.Equal(HttpStatusCode.NotFound, missingNvr.StatusCode);

        using var mismatch = await client.GetAsync(
            $"/api/alarms?nvrId={nvrId}&cameraId=missing" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");
        Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Alarm_search_rejects_a_camera_from_another_nvr()
    {
        var fake = new FakeAlarmLogClient();
        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var createNvr = await client.PostAsJsonAsync(
            "/api/nvrs",
            new { name = "Other NVR", baseUrl = "rtsp://192.168.1.65:554" });
        var other = await createNvr.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(other);

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}&nvrId={other.Id}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Alarm_search_isolates_a_failed_nvr_when_searching_all_cameras()
    {
        var fake = new FakeAlarmLogClient
        {
            Result = new IsapiAlarmLogResult(
                [
                    Start(1, 10, 0, 0, "Osoba"),
                    Stop(1, 10, 0, 7, "Osoba"),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, _, _) = await SetupAsync(host);
        using var _ = client;

        using var createNvr = await client.PostAsJsonAsync(
            "/api/nvrs",
            new { name = "Offline NVR", baseUrl = "rtsp://192.168.1.66:554" });
        var offline = await createNvr.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(offline);

        using var createCamera = await client.PostAsJsonAsync(
            $"/api/nvrs/{offline.Id}/cameras",
            new { name = "Offline camera", mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.Created, createCamera.StatusCode);

        fake.FailNvrId = offline.Id;

        using var response = await client.GetAsync(
            "/api/alarms?start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AlarmSearchResponse>();
        Assert.NotNull(result);
        Assert.Single(result.Ranges);
        var error = Assert.Single(result.Errors);
        Assert.Equal(offline.Id, error.NvrId);
        Assert.Contains("unreachable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Alarm_search_allows_non_admin_viewers()
    {
        var fake = new FakeAlarmLogClient();
        using var host = new ApiTestHost(alarmLogClient: fake);
        var (admin, _, _) = await SetupAsync(host);
        using var _ = admin;

        var username = $"viewer-{Guid.NewGuid():N}";
        using var createUser = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                username,
                password = "viewer-password-123",
                isAdmin = false,
            });
        createUser.EnsureSuccessStatusCode();

        using var viewer = await host.CreateAuthenticatedClientAsync(
            username,
            "viewer-password-123");
        using var response = await viewer.GetAsync(
            "/api/alarms?start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(IsapiFailureKind.BadRequest, HttpStatusCode.BadRequest)]
    [InlineData(IsapiFailureKind.Unauthorized, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.NotFound, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.Unreachable, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.InvalidResponse, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.DeviceError, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.Timeout, HttpStatusCode.GatewayTimeout)]
    public async Task Alarm_search_maps_upstream_failures_without_a_peekaboo_401(
        IsapiFailureKind failureKind,
        HttpStatusCode expectedStatus)
    {
        var fake = new FakeAlarmLogClient
        {
            Failure = new IsapiException("The NVR failed.", failureKind),
        };

        using var host = new ApiTestHost(alarmLogClient: fake);
        var (client, cameraId, _) = await SetupAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/alarms?cameraId={cameraId}" +
            "&start=2026-11-21T10:00:00Z&end=2026-11-21T11:00:00Z");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(HttpClient Client, string CameraId, string NvrId)> SetupAsync(
        ApiTestHost host)
    {
        var client = await host.CreateAdminClientAsync();

        using var createNvr = await client.PostAsJsonAsync(
            "/api/nvrs",
            new { name = "House NVR", baseUrl = "rtsp://192.168.1.64:554" });
        Assert.Equal(HttpStatusCode.Created, createNvr.StatusCode);
        var nvr = await createNvr.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(nvr);

        using var createCamera = await client.PostAsJsonAsync(
            $"/api/nvrs/{nvr.Id}/cameras",
            new { name = "Front door", mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.Created, createCamera.StatusCode);
        var camera = await createCamera.Content.ReadFromJsonAsync<IdResponse>();
        Assert.NotNull(camera);

        using var createSecond = await client.PostAsJsonAsync(
            $"/api/nvrs/{nvr.Id}/cameras",
            new { name = "Garage", mainStreamId = "201", subStreamId = "202" });
        Assert.Equal(HttpStatusCode.Created, createSecond.StatusCode);

        return (client, camera.Id, nvr.Id);
    }

    private static IsapiAlarmLogEntry Start(
        int channel,
        int hour,
        int minute,
        int second,
        string? target) =>
        new(
            IsapiAlarmLogKind.MotionStart,
            new DateTimeOffset(2026, 11, 21, hour, minute, second, TimeSpan.Zero),
            channel,
            target,
            $"log.hikvision.com/Alarm/motionStart/{channel}");

    private static IsapiAlarmLogEntry Stop(
        int channel,
        int hour,
        int minute,
        int second,
        string? target) =>
        new(
            IsapiAlarmLogKind.MotionStop,
            new DateTimeOffset(2026, 11, 21, hour, minute, second, TimeSpan.Zero),
            channel,
            target,
            $"log.hikvision.com/Alarm/motionStop/{channel}");

    private sealed record IdResponse(string Id);

    private sealed record AlarmRangeResponse(
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

    private sealed record AlarmSearchResponse(
        DateTimeOffset Start,
        DateTimeOffset End,
        bool Truncated,
        List<AlarmRangeResponse> Ranges,
        List<AlarmSearchErrorResponse> Errors);

    private sealed record AlarmSearchErrorResponse(
        string NvrId,
        string NvrName,
        string Message);

    private sealed class FakeAlarmLogClient : IIsapiAlarmLogClient
    {
        public IsapiAlarmLogResult Result { get; set; } = new([], Truncated: false);

        public Exception? Failure { get; set; }

        public string? FailNvrId { get; set; }

        public List<IsapiAlarmLogRequest> Requests { get; } = [];

        public Task<IsapiAlarmLogResult> SearchAsync(
            IsapiAlarmLogRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (FailNvrId is not null &&
                string.Equals(request.NvrId, FailNvrId, StringComparison.Ordinal))
            {
                return Task.FromException<IsapiAlarmLogResult>(
                    new IsapiException("The NVR is unreachable.", IsapiFailureKind.Unreachable));
            }

            return Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<IsapiAlarmLogResult>(Failure);
        }
    }
}
