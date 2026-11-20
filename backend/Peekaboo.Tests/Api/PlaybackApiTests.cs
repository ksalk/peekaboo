using System.Net;
using System.Net.Http.Json;
using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class PlaybackApiTests
{
    private sealed record IdResponse(string Id);

    private sealed record RecordingSegmentResponse(
        DateTimeOffset Start,
        DateTimeOffset End,
        int DurationSeconds,
        string? CodecType,
        string PlaybackUrl);

    private sealed record RecordingSearchResponse(
        string CameraId,
        string CameraName,
        DateTimeOffset Start,
        DateTimeOffset End,
        bool Truncated,
        List<RecordingSegmentResponse> Recordings);

    [Fact]
    public async Task Recording_search_requires_authentication()
    {
        using var host = new ApiTestHost(isapiClient: new FakeIsapiClient());
        using var client = host.CreateClient();

        using var response = await client.GetAsync(
            "/api/cameras/missing/recordings" +
            "?start=2026-11-20T00:00:00Z&end=2026-11-21T00:00:00Z");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Recording_search_returns_segments_and_canonical_urls()
    {
        var fake = new FakeIsapiClient
        {
            Result = new IsapiSearchResult(
                [
                    new IsapiRecordingSegment(
                        new DateTimeOffset(2026, 11, 20, 10, 32, 1, TimeSpan.Zero),
                        new DateTimeOffset(2026, 11, 20, 10, 40, 42, TimeSpan.Zero),
                        "H.264-BP"),
                ],
                Truncated: true),
        };

        using var host = new ApiTestHost(isapiClient: fake);
        var (client, cameraId) = await SetupCameraAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings" +
            "?start=2026-11-20T00:00:00Z&end=2026-11-21T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RecordingSearchResponse>();
        Assert.NotNull(result);
        Assert.Equal(cameraId, result.CameraId);
        Assert.Equal("Front door", result.CameraName);
        Assert.True(result.Truncated);

        var segment = Assert.Single(result.Recordings);
        Assert.Equal(521, segment.DurationSeconds);
        Assert.Equal("H.264-BP", segment.CodecType);
        Assert.Equal(
            "rtsp://192.168.1.64:554/ISAPI/Streaming/tracks/101" +
            "?starttime=20261120T103201Z&endtime=20261120T104042Z",
            segment.PlaybackUrl);

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("101", fake.LastRequest.TrackId);
        Assert.Equal("rtsp://192.168.1.64:554", fake.LastRequest.RtspBaseUrl);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero),
            fake.LastRequest.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
            fake.LastRequest.End);
    }

    [Fact]
    public async Task Recording_search_drops_invalid_segments_and_rounds_durations()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var fake = new FakeIsapiClient
        {
            Result = new IsapiSearchResult(
                [
                    new IsapiRecordingSegment(start, start, "H.264-BP"),
                    new IsapiRecordingSegment(start, start.AddSeconds(1.6), "H.264-BP"),
                ],
                Truncated: false),
        };

        using var host = new ApiTestHost(isapiClient: fake);
        var (client, cameraId) = await SetupCameraAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings" +
            "?start=2026-11-20T00:00:00Z&end=2026-11-21T00:00:00Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RecordingSearchResponse>();
        Assert.NotNull(result);
        var segment = Assert.Single(result.Recordings);
        Assert.Equal(2, segment.DurationSeconds);
    }

    [Theory]
    [InlineData("", "", "required")]
    [InlineData(
        "2026-11-20T10:00:00Z",
        "2026-11-20T10:00:00Z",
        "after")]
    [InlineData(
        "2026-11-01T00:00:00Z",
        "2026-12-10T00:00:00Z",
        "31")]
    public async Task Recording_search_rejects_invalid_ranges(
        string start,
        string end,
        string expectedErrorFragment)
    {
        var fake = new FakeIsapiClient();
        using var host = new ApiTestHost(isapiClient: fake);
        var (client, cameraId) = await SetupCameraAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings" +
            $"?start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedErrorFragment, body, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fake.LastRequest);
    }

    [Fact]
    public async Task Recording_search_returns_not_found_for_a_missing_camera()
    {
        var fake = new FakeIsapiClient();
        using var host = new ApiTestHost(isapiClient: fake);
        using var client = await host.CreateAdminClientAsync();

        using var response = await client.GetAsync(
            "/api/cameras/missing/recordings" +
            "?start=2026-11-20T00:00:00Z&end=2026-11-21T00:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(fake.LastRequest);
    }

    [Theory]
    [InlineData(IsapiFailureKind.BadRequest, HttpStatusCode.BadRequest)]
    [InlineData(IsapiFailureKind.Unauthorized, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.NotFound, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.Unreachable, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.InvalidResponse, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.DeviceError, HttpStatusCode.BadGateway)]
    [InlineData(IsapiFailureKind.Timeout, HttpStatusCode.GatewayTimeout)]
    public async Task Recording_search_maps_upstream_failures_without_a_peekaboo_401(
        IsapiFailureKind failureKind,
        HttpStatusCode expectedStatus)
    {
        var fake = new FakeIsapiClient
        {
            Failure = new IsapiException("The NVR failed.", failureKind),
        };

        using var host = new ApiTestHost(isapiClient: fake);
        var (client, cameraId) = await SetupCameraAsync(host);
        using var _ = client;

        using var response = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings" +
            "?start=2026-11-20T00:00:00Z&end=2026-11-21T00:00:00Z");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(HttpClient Client, string CameraId)> SetupCameraAsync(
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

        return (client, camera.Id);
    }

    private sealed class FakeIsapiClient : IIsapiClient
    {
        public IsapiSearchResult Result { get; set; } = new([], Truncated: false);

        public Exception? Failure { get; set; }

        public IsapiSearchRequest? LastRequest { get; private set; }

        public Task<IsapiSearchResult> SearchRecordingsAsync(
            IsapiSearchRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<IsapiSearchResult>(Failure);
        }
    }
}
