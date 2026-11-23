using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class StreamApiTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-stream-tests-").FullName;

    public StreamApiTests()
    {
        File.WriteAllBytes(SamplePath, BuildSample());
        File.WriteAllBytes(AudioSamplePath, BuildSample(includeAudio: true));
        File.WriteAllText(
            ScriptPath,
            $"""
            #!/bin/sh
            echo "$$" >> "{StartLogPath}"
            printf '%s\n' "$*" >> "{ArgsLogPath}"
            cat "{SamplePath}"
            exec sleep 300
            """);
        File.WriteAllText(
            AudioScriptPath,
            $"""
            #!/bin/sh
            cat "{AudioSamplePath}"
            exec sleep 300
            """);
        File.WriteAllText(
            FiniteScriptPath,
            $"""
            #!/bin/sh
            echo "$$" >> "{StartLogPath}"
            printf '%s\n' "$*" >> "{ArgsLogPath}"
            cat "{SamplePath}"
            exit 0
            """);
        File.WriteAllText(
            FailScriptPath,
            """
            #!/bin/sh
            echo "rtsp: 401 Unauthorized" >&2
            exit 1
            """);

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(ScriptPath);
            MakeExecutable(AudioScriptPath);
            MakeExecutable(FiniteScriptPath);
            MakeExecutable(FailScriptPath);
        }
    }

    private string SamplePath => Path.Combine(_directory, "sample.fmp4");

    private string AudioSamplePath => Path.Combine(_directory, "audio-sample.fmp4");

    private string AudioScriptPath => Path.Combine(_directory, "audio-ffmpeg.sh");

    private string StartLogPath => Path.Combine(_directory, "starts.log");

    private string ArgsLogPath => Path.Combine(_directory, "args.log");

    private string ScriptPath => Path.Combine(_directory, "fake-ffmpeg.sh");

    private string FiniteScriptPath => Path.Combine(_directory, "finite-ffmpeg.sh");

    private string FailScriptPath => Path.Combine(_directory, "fail-ffmpeg.sh");

    private sealed record NvrResponse(string Id, string Name, string BaseUrl);

    private sealed record CameraResponse(
        string Id,
        string NvrId,
        string Name,
        string MainStreamId,
        string SubStreamId);

    [Fact]
    public async Task Stream_starts_on_first_websocket_and_stops_after_close()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        var codec = await ReceiveReadyAsync(socket);
        Assert.Equal("avc1.64001f", codec);

        var init = await ReceiveBinaryAsync(socket);
        var initTypes = Encoding.ASCII.GetString(init);
        Assert.Contains("ftyp", initTypes, StringComparison.Ordinal);
        Assert.Contains("moov", initTypes, StringComparison.Ordinal);

        Assert.Equal("moof", await ReceiveBoxAsync(socket));
        Assert.Equal("mdat", await ReceiveBoxAsync(socket));

        var pid = await WaitForStartedProcessAsync();
        Assert.True(ProcessAlive(pid), $"fake ffmpeg process {pid} should be running");

        await CloseQuietlyAsync(socket);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Ready_message_includes_audio_codec_when_the_stream_has_audio()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(scriptPath: AudioScriptPath);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        var (codec, audioCodec) = await ReceiveReadyMessageAsync(socket);

        Assert.Equal("avc1.64001f", codec);
        Assert.Equal("mp4a.40.2", audioCodec);

        await CloseQuietlyAsync(socket);
    }

    [Fact]
    public async Task Two_viewers_share_one_ffmpeg_process_and_stop_after_the_last_leaves()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var first = await ConnectAsync(host, cameraId, cookie);
        using var second = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(first);
        await ReceiveBinaryAsync(first);
        await ReceiveReadyAsync(second);
        await ReceiveBinaryAsync(second);

        var pid = await WaitForStartedProcessAsync();
        Assert.Single(await ReadStartLogAsync());

        await CloseQuietlyAsync(first);
        await Task.Delay(500);
        Assert.True(ProcessAlive(pid), "one remaining viewer must keep the stream alive");

        await CloseQuietlyAsync(second);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Deleting_a_camera_stops_its_live_stream()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (client, nvrId, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        var pid = await WaitForStartedProcessAsync();

        var errorTask = ReceiveTextMessageAsync(socket);

        using var delete = await client.DeleteAsync($"/api/nvrs/{nvrId}/cameras/{cameraId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var error = await errorTask;
        Assert.Contains("deleted", error, StringComparison.OrdinalIgnoreCase);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Stream_limit_rejects_additional_cameras()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(maxStreams: 1);
        var (_, _, firstCameraId, cookie) = await SetupCameraAsync(host, "First");
        var (_, _, secondCameraId, _) = await SetupCameraAsync(host, "Second");

        using var first = await ConnectAsync(host, firstCameraId, cookie);
        await ReceiveReadyAsync(first);
        await ReceiveBinaryAsync(first);

        using var second = await ConnectAsync(host, secondCameraId, cookie);
        var error = await ReceiveTextMessageAsync(second);
        Assert.Contains("Stream limit", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await ReadStartLogAsync());

        var pid = await WaitForStartedProcessAsync();
        await CloseQuietlyAsync(first);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Viewer_limit_is_enforced_per_camera()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(maxViewers: 1);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var first = await ConnectAsync(host, cameraId, cookie);
        await ReceiveReadyAsync(first);
        await ReceiveBinaryAsync(first);

        using var second = await ConnectAsync(host, cameraId, cookie);
        var error = await ReceiveTextMessageAsync(second);
        Assert.Contains("Too many viewers", error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await ReadStartLogAsync());

        var pid = await WaitForStartedProcessAsync();
        await CloseQuietlyAsync(first);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Ffmpeg_failure_is_reported_to_the_client()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(scriptPath: FailScriptPath);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        var error = await ReceiveTextMessageAsync(socket);

        Assert.Contains("exit code 1", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("401 Unauthorized", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Main_and_sub_streams_start_separate_ffmpeg_processes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var main = await ConnectAsync(host, cameraId, cookie, "main");
        using var sub = await ConnectAsync(host, cameraId, cookie, "sub");

        await ReceiveReadyAsync(main);
        await ReceiveBinaryAsync(main);
        await ReceiveReadyAsync(sub);
        await ReceiveBinaryAsync(sub);

        await WaitForStartCountAsync(2);

        await CloseQuietlyAsync(main);
        await CloseQuietlyAsync(sub);
        await WaitForAllProcessesGoneAsync();
    }

    [Fact]
    public async Task Isapi_url_and_credentials_are_passed_to_ffmpeg()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(withCredentials: true);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie, "main");

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);

        var arguments = await WaitForArgsAsync();

        Assert.Contains(
            "-i rtsp://viewer:secret@fake.local:554/ISAPI/Streaming/channels/101",
            arguments,
            StringComparison.Ordinal);

        await CloseQuietlyAsync(socket);
        await WaitForAllProcessesGoneAsync();
    }

    [Fact]
    public async Task Invalid_stream_query_is_rejected()
    {
        using var host = CreateHost();
        var (client, _, cameraId, _) = await SetupCameraAsync(host);

        using var response = await client.GetAsync(
            $"/api/cameras/{cameraId}/stream?stream=both");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "main",
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_camera_stream_returns_not_found()
    {
        using var host = CreateHost();
        var (client, _, _, _) = await SetupCameraAsync(host);

        using var response = await client.GetAsync("/api/cameras/missing/stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_nvr_stops_its_live_stream()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (client, nvrId, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        var pid = await WaitForStartedProcessAsync();

        var errorTask = ReceiveTextMessageAsync(socket);

        using var delete = await client.DeleteAsync($"/api/nvrs/{nvrId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var error = await errorTask;
        Assert.Contains("NVR was deleted", error, StringComparison.OrdinalIgnoreCase);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Updating_camera_stream_ids_stops_its_live_stream()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (client, nvrId, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        var pid = await WaitForStartedProcessAsync();

        var errorTask = ReceiveTextMessageAsync(socket);

        using var update = await client.PutAsJsonAsync(
            $"/api/nvrs/{nvrId}/cameras/{cameraId}",
            new { name = "Test camera", mainStreamId = "201", subStreamId = "202" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var error = await errorTask;
        Assert.Contains("updated", error, StringComparison.OrdinalIgnoreCase);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Changing_an_nvr_base_url_stops_its_live_stream()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (client, nvrId, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        var pid = await WaitForStartedProcessAsync();

        var errorTask = ReceiveTextMessageAsync(socket);

        using var update = await client.PutAsJsonAsync(
            $"/api/nvrs/{nvrId}",
            new { name = "Test NVR", baseUrl = "rtsp://fake2.local:554" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var error = await errorTask;
        Assert.Contains("updated", error, StringComparison.OrdinalIgnoreCase);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Name_only_updates_do_not_stop_live_streams()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (client, nvrId, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        var pid = await WaitForStartedProcessAsync();

        using var updateNvr = await client.PutAsJsonAsync(
            $"/api/nvrs/{nvrId}",
            new { name = "Renamed NVR", baseUrl = "rtsp://fake.local:554" });
        Assert.Equal(HttpStatusCode.OK, updateNvr.StatusCode);

        using var updateCamera = await client.PutAsJsonAsync(
            $"/api/nvrs/{nvrId}/cameras/{cameraId}",
            new { name = "Renamed camera", mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.OK, updateCamera.StatusCode);

        await Task.Delay(300);
        Assert.True(ProcessAlive(pid), "name-only updates must not restart the stream");

        await CloseQuietlyAsync(socket);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Main_and_sub_count_toward_the_stream_limit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(maxStreams: 1);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var sub = await ConnectAsync(host, cameraId, cookie);
        await ReceiveReadyAsync(sub);
        await ReceiveBinaryAsync(sub);

        using var main = await ConnectAsync(host, cameraId, cookie, "main");
        var error = await ReceiveTextMessageAsync(main);
        Assert.Contains("Stream limit", error, StringComparison.OrdinalIgnoreCase);

        var pid = await WaitForStartedProcessAsync();
        await CloseQuietlyAsync(sub);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Recorded_segment_stream_ends_cleanly_and_caps_the_duration()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(scriptPath: FiniteScriptPath);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var socket = await ConnectPlaybackAsync(
            host,
            cameraId,
            cookie,
            "2026-11-20T10:00:00Z",
            "2026-11-20T10:30:00Z");

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        Assert.Equal("moof", await ReceiveBoxAsync(socket));
        Assert.Equal("mdat", await ReceiveBoxAsync(socket));

        var ended = await ReceiveTextMessageAsync(socket);
        Assert.Contains("\"type\":\"ended\"", ended, StringComparison.Ordinal);

        using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var close = await socket.ReceiveAsync(new byte[1024], closeTimeout.Token);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, close.CloseStatus);

        var arguments = await WaitForArgsAsync();
        Assert.Contains("-t 1800", arguments, StringComparison.Ordinal);
        Assert.Contains("starttime=20261120T100000Z", arguments, StringComparison.Ordinal);
        Assert.Contains("endtime=20261120T103000Z", arguments, StringComparison.Ordinal);

        var pid = await WaitForStartedProcessAsync();
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Playback_failure_is_reported_as_an_error_not_ended()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(scriptPath: FailScriptPath);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var socket = await ConnectPlaybackAsync(
            host,
            cameraId,
            cookie,
            "2026-11-20T10:00:00Z",
            "2026-11-20T10:30:00Z");

        var message = await ReceiveTextMessageAsync(socket);
        Assert.Contains("\"type\":\"error\"", message, StringComparison.Ordinal);
        Assert.Contains("exit code 1", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"type\":\"ended\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Playback_viewers_with_the_same_range_share_one_process()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var first = await ConnectPlaybackAsync(
            host,
            cameraId,
            cookie,
            "2026-11-20T10:00:00Z",
            "2026-11-20T10:30:00Z");
        using var second = await ConnectPlaybackAsync(
            host,
            cameraId,
            cookie,
            "2026-11-20T10:00:00Z",
            "2026-11-20T10:30:00Z");

        await ReceiveReadyAsync(first);
        await ReceiveBinaryAsync(first);
        await ReceiveReadyAsync(second);
        await ReceiveBinaryAsync(second);

        var pid = await WaitForStartedProcessAsync();
        Assert.Single(await ReadStartLogAsync());

        await CloseQuietlyAsync(first);
        await Task.Delay(200);
        Assert.True(ProcessAlive(pid), "one remaining viewer must keep the playback alive");

        await CloseQuietlyAsync(second);
        await WaitForProcessGoneAsync(pid);
    }

    [Fact]
    public async Task Playback_counts_toward_the_stream_limit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost(maxStreams: 1);
        var (_, _, cameraId, cookie) = await SetupCameraAsync(host);

        using var live = await ConnectAsync(host, cameraId, cookie);
        await ReceiveReadyAsync(live);
        await ReceiveBinaryAsync(live);

        using var playback = await ConnectPlaybackAsync(
            host,
            cameraId,
            cookie,
            "2026-11-20T10:00:00Z",
            "2026-11-20T10:30:00Z");

        var message = await ReceiveTextMessageAsync(playback);
        Assert.Contains("Stream limit", message, StringComparison.OrdinalIgnoreCase);

        await CloseQuietlyAsync(live);
        await WaitForAllProcessesGoneAsync();
    }

    [Fact]
    public async Task Recorded_segment_stream_rejects_invalid_ranges_and_cameras()
    {
        using var host = CreateHost();
        var (client, _, cameraId, _) = await SetupCameraAsync(host);

        using var invalidRange = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings/stream" +
            "?start=2026-11-20T10:00:00Z&end=2026-11-20T10:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);

        using var tooLong = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings/stream" +
            "?start=2026-11-01T00:00:00Z&end=2026-11-02T01:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        using var missingCamera = await client.GetAsync(
            "/api/cameras/missing/recordings/stream" +
            "?start=2026-11-20T10:00:00Z&end=2026-11-20T10:30:00Z");
        Assert.Equal(HttpStatusCode.NotFound, missingCamera.StatusCode);

        using var notAWebSocket = await client.GetAsync(
            $"/api/cameras/{cameraId}/recordings/stream" +
            "?start=2026-11-20T10:00:00Z&end=2026-11-20T10:30:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, notAWebSocket.StatusCode);
    }

    [Fact]
    public async Task Credentials_are_not_stored_or_returned()
    {
        using var host = CreateHost(withCredentials: true);
        var (client, _, _, _) = await SetupCameraAsync(host);

        var nvrs = await client.GetStringAsync("/api/nvrs");
        var cameras = await client.GetStringAsync("/api/cameras");

        Assert.DoesNotContain("secret", nvrs, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", cameras, StringComparison.Ordinal);

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = host.DatabasePath + suffix;
            if (!File.Exists(path))
            {
                continue;
            }

            var contents = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            Assert.DoesNotContain("secret", contents, StringComparison.Ordinal);
        }
    }

    private ApiTestHost CreateHost(
        int maxStreams = 9,
        int maxViewers = 8,
        string? scriptPath = null,
        bool withCredentials = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["PEEKABOO_FFMPEG_PATH"] = scriptPath ?? ScriptPath,
            ["PEEKABOO_MAX_STREAMS"] = maxStreams.ToString(CultureInfo.InvariantCulture),
            ["PEEKABOO_MAX_VIEWERS_PER_STREAM"] = maxViewers.ToString(CultureInfo.InvariantCulture),
        };

        if (withCredentials)
        {
            settings["CAMERA_USERNAME"] = "viewer";
            settings["CAMERA_PASSWORD"] = "secret";
        }

        return new ApiTestHost(settings);
    }

    private async Task<(HttpClient Client, string NvrId, string CameraId, string Cookie)> SetupCameraAsync(
        ApiTestHost host,
        string name = "Test camera")
    {
        var client = host.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiTestHost.AdminUsername, password = ApiTestHost.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        using var createNvr = await client.PostAsJsonAsync(
            "/api/nvrs",
            new { name = "Test NVR", baseUrl = "rtsp://fake.local:554" });
        Assert.Equal(HttpStatusCode.Created, createNvr.StatusCode);

        var nvr = await createNvr.Content.ReadFromJsonAsync<NvrResponse>();
        Assert.NotNull(nvr);

        using var create = await client.PostAsJsonAsync(
            $"/api/nvrs/{nvr.Id}/cameras",
            new { name, mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var camera = await create.Content.ReadFromJsonAsync<CameraResponse>();
        Assert.NotNull(camera);

        return (client, nvr.Id, camera.Id, cookie);
    }

    private static async Task<WebSocket> ConnectAsync(
        ApiTestHost host,
        string cameraId,
        string cookie,
        string? stream = null)
    {
        var client = CreateWebSocketClient(host, cookie);
        var query = stream is null ? string.Empty : $"?stream={Uri.EscapeDataString(stream)}";

        return await client.ConnectAsync(
            new Uri($"ws://localhost/api/cameras/{cameraId}/stream{query}"),
            CancellationToken.None);
    }

    private static async Task<WebSocket> ConnectPlaybackAsync(
        ApiTestHost host,
        string cameraId,
        string cookie,
        string start,
        string end)
    {
        var client = CreateWebSocketClient(host, cookie);

        return await client.ConnectAsync(
            new Uri(
                $"ws://localhost/api/cameras/{cameraId}/recordings/stream" +
                $"?start={Uri.EscapeDataString(start)}&end={Uri.EscapeDataString(end)}"),
            CancellationToken.None);
    }

    private static WebSocketClient CreateWebSocketClient(ApiTestHost host, string cookie)
    {
        var client = host.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers["Cookie"] = cookie;
            request.Headers["Origin"] = "http://localhost";
        };

        return client;
    }

    private static async Task<string> ReceiveReadyAsync(WebSocket socket) =>
        (await ReceiveReadyMessageAsync(socket)).Codec;

    private static async Task<(string Codec, string? AudioCodec)> ReceiveReadyMessageAsync(
        WebSocket socket)
    {
        var (type, data) = await ReceiveMessageAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, type);

        using var document = JsonDocument.Parse(data);
        Assert.Equal("ready", document.RootElement.GetProperty("type").GetString());

        var codec = document.RootElement.GetProperty("codec").GetString()!;
        var audioCodec = document.RootElement.TryGetProperty("audioCodec", out var audioProperty) &&
                         audioProperty.ValueKind != JsonValueKind.Null
            ? audioProperty.GetString()
            : null;

        return (codec, audioCodec);
    }

    private static async Task<string> ReceiveTextMessageAsync(WebSocket socket)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var (type, data) = await ReceiveMessageAsync(socket);
            if (type == WebSocketMessageType.Text)
            {
                return Encoding.UTF8.GetString(data);
            }
        }

        throw new InvalidOperationException("No text message received.");
    }

    private static async Task<string> ReceiveBoxAsync(WebSocket socket)
    {
        var data = await ReceiveBinaryAsync(socket);
        return Encoding.ASCII.GetString(data, 4, 4);
    }

    private static async Task<byte[]> ReceiveBinaryAsync(WebSocket socket)
    {
        var (type, data) = await ReceiveMessageAsync(socket);
        Assert.Equal(WebSocketMessageType.Binary, type);
        Assert.True(data.Length >= 8);
        return data;
    }

    private static async Task<(WebSocketMessageType Type, byte[] Data)> ReceiveMessageAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            message.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return (result.MessageType, message.ToArray());
            }

            Assert.True(message.Length < 4 * 1024 * 1024, "WebSocket message too large for test.");
        }
    }

    private async Task<int> WaitForStartedProcessAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var lines = await ReadStartLogAsync();
            if (lines.Count > 0 && int.TryParse(lines[^1], out var pid))
            {
                return pid;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("Fake ffmpeg did not start.");
    }

    private async Task<List<string>> ReadStartLogAsync()
    {
        if (!File.Exists(StartLogPath))
        {
            return [];
        }

        try
        {
            return [.. (await File.ReadAllLinesAsync(StartLogPath))
                .Where(line => !string.IsNullOrWhiteSpace(line))];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private async Task<string> WaitForArgsAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(ArgsLogPath))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(ArgsLogPath);
                    if (lines.Length > 0)
                    {
                        return lines[^1];
                    }
                }
                catch (IOException)
                {
                }
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("Fake ffmpeg did not log its arguments.");
    }

    private async Task WaitForStartCountAsync(int count)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await ReadStartLogAsync()).Count >= count)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException($"Expected at least {count} ffmpeg starts.");
    }

    private async Task WaitForAllProcessesGoneAsync()
    {
        var pids = (await ReadStartLogAsync())
            .Select(line => int.TryParse(line, out var pid) ? pid : 0)
            .Where(pid => pid > 0);

        foreach (var pid in pids)
        {
            await WaitForProcessGoneAsync(pid);
        }
    }

    private static bool ProcessAlive(int pid) => Directory.Exists($"/proc/{pid}");

    private static async Task WaitForProcessGoneAsync(int pid)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (!ProcessAlive(pid))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"fake ffmpeg process {pid} is still running");
    }

    private static async Task CloseQuietlyAsync(WebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test finished", timeout.Token);
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static byte[] BuildSample(bool includeAudio = false)
    {
        var avcC = Box("avcC", [1, 0x64, 0x00, 0x1f, 0xff, 0xe1, 0x00, 0x00]);
        var moovChildren = new List<byte[]> { Box("mvhd", new byte[8]), avcC };

        if (includeAudio)
        {
            var hdlr = Box(
                "hdlr",
                [0, 0, 0, 0, 0, 0, 0, 0, (byte)'s', (byte)'o', (byte)'u', (byte)'n', 0, 0, 0, 0]);
            var stsd = Box("stsd", [0, 0, 0, 0, 0, 0, 0, 1, .. Box("mp4a", new byte[28])]);
            var mdia = Box(
                "mdia",
                [.. hdlr, .. Box("minf", [.. Box("stbl", [.. stsd])])]);
            moovChildren.Add(Box("trak", [.. mdia]));
        }

        var moov = Box("moov", [.. moovChildren.SelectMany(part => part)]);
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom0000"));
        var moof = Box("moof", new byte[16]);
        var mdat = Box("mdat", Encoding.ASCII.GetBytes("video-payload"));

        return [.. ftyp, .. moov, .. moof, .. mdat];
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var data = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(data, 4);
        payload.CopyTo(data, 8);
        return data;
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
