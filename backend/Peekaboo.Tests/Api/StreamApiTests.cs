using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class StreamApiTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-stream-tests-").FullName;

    public StreamApiTests()
    {
        File.WriteAllBytes(SamplePath, BuildSample());
        File.WriteAllText(
            ScriptPath,
            $"""
            #!/bin/sh
            echo "$$" >> "{StartLogPath}"
            cat "{SamplePath}"
            exec sleep 300
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
            MakeExecutable(FailScriptPath);
        }
    }

    private string SamplePath => Path.Combine(_directory, "sample.fmp4");

    private string StartLogPath => Path.Combine(_directory, "starts.log");

    private string ScriptPath => Path.Combine(_directory, "fake-ffmpeg.sh");

    private string FailScriptPath => Path.Combine(_directory, "fail-ffmpeg.sh");

    private sealed record CameraResponse(string Id, string Name, string Url);

    [Fact]
    public async Task Stream_starts_on_first_websocket_and_stops_after_close()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (_, cameraId, cookie) = await SetupCameraAsync(host);
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
    public async Task Two_viewers_share_one_ffmpeg_process_and_stop_after_the_last_leaves()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var host = CreateHost();
        var (_, cameraId, cookie) = await SetupCameraAsync(host);

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
        var (client, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        await ReceiveReadyAsync(socket);
        await ReceiveBinaryAsync(socket);
        var pid = await WaitForStartedProcessAsync();

        var errorTask = ReceiveTextMessageAsync(socket);

        using var delete = await client.DeleteAsync($"/api/cameras/{cameraId}");
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
        var (_, firstCameraId, cookie) = await SetupCameraAsync(host, "First");
        var (_, secondCameraId, _) = await SetupCameraAsync(host, "Second");

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
        var (_, cameraId, cookie) = await SetupCameraAsync(host);

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
        var (_, cameraId, cookie) = await SetupCameraAsync(host);
        using var socket = await ConnectAsync(host, cameraId, cookie);

        var error = await ReceiveTextMessageAsync(socket);

        Assert.Contains("exit code 1", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("401 Unauthorized", error, StringComparison.OrdinalIgnoreCase);
    }

    private ApiTestHost CreateHost(
        int maxStreams = 9,
        int maxViewers = 8,
        string? scriptPath = null) =>
        new(new Dictionary<string, string?>
        {
            ["PEEKABOO_FFMPEG_PATH"] = scriptPath ?? ScriptPath,
            ["PEEKABOO_MAX_STREAMS"] = maxStreams.ToString(CultureInfo.InvariantCulture),
            ["PEEKABOO_MAX_VIEWERS_PER_STREAM"] = maxViewers.ToString(CultureInfo.InvariantCulture),
        });

    private async Task<(HttpClient Client, string CameraId, string Cookie)> SetupCameraAsync(
        ApiTestHost host,
        string name = "Test camera")
    {
        var client = host.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiTestHost.AdminUsername, password = ApiTestHost.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        using var create = await client.PostAsJsonAsync(
            "/api/cameras",
            new { name, url = "rtsp://fake.local/stream" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var camera = await create.Content.ReadFromJsonAsync<CameraResponse>();
        Assert.NotNull(camera);

        return (client, camera.Id, cookie);
    }

    private static async Task<WebSocket> ConnectAsync(ApiTestHost host, string cameraId, string cookie)
    {
        var client = host.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers["Cookie"] = cookie;
            request.Headers["Origin"] = "http://localhost";
        };

        return await client.ConnectAsync(
            new Uri($"ws://localhost/api/cameras/{cameraId}/stream"),
            CancellationToken.None);
    }

    private static async Task<string> ReceiveReadyAsync(WebSocket socket)
    {
        var (type, data) = await ReceiveMessageAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, type);

        using var document = JsonDocument.Parse(data);
        Assert.Equal("ready", document.RootElement.GetProperty("type").GetString());

        return document.RootElement.GetProperty("codec").GetString()!;
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

    private static byte[] BuildSample()
    {
        var avcC = Box("avcC", [1, 0x64, 0x00, 0x1f, 0xff, 0xe1, 0x00, 0x00]);
        var moov = Box("moov", [.. Box("mvhd", new byte[8]), .. avcC]);
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
