using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class StreamSessionTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-session-tests-").FullName;

    [Fact]
    public async Task Start_after_stop_never_launches_ffmpeg()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var startLog = CreateScript("echo \"$$\" >> \"" + StartLogPath + "\"\nexec sleep 300");
        var terminated = false;
        var session = CreateSession(startLog, () => terminated = true);
        await session.StopAsync(errorMessage: null);
        Assert.True(terminated);

        session.Start();
        await Task.Delay(500);

        Assert.False(File.Exists(StartLogPath), "ffmpeg must not start after the session stopped");
    }

    [Fact]
    public async Task Stop_terminates_a_running_ffmpeg_process()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var script = CreateScript("echo \"$$\" > \"" + StartLogPath + "\"\nexec sleep 300");
        var session = CreateSession(script, onTerminated: null);

        session.Start();

        var pid = await WaitForPidAsync();
        Assert.True(Directory.Exists($"/proc/{pid}"));

        await session.StopAsync(errorMessage: null);

        Assert.False(Directory.Exists($"/proc/{pid}"), $"ffmpeg process {pid} should be terminated");
    }

    [Fact]
    public async Task Sdk_input_is_piped_to_ffmpeg_stdin()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var stdinPath = Path.Combine(_directory, "stdin.bin");
        var argsPath = Path.Combine(_directory, "args.txt");
        var script = CreateScript(
            $"printf '%s\\n' \"$@\" > \"{argsPath}\"\n" +
            $"cat > \"{stdinPath}\"\n" +
            "exec sleep 300");
        var payload = new byte[] { 0, 0, 1, 0xba, 1, 2, 3, 4 };
        var input = new RecordingInput(payload);
        var session = CreateSession(
            script,
            onTerminated: null,
            new HikvisionLiveStreamSource("nvr", "192.168.10.60", 8000, 1, 0),
            input);

        session.Start();
        await WaitForFileLengthAsync(stdinPath, payload.Length);

        var arguments = await File.ReadAllLinesAsync(argsPath);
        Assert.Contains("mpeg", arguments);
        Assert.Contains("pipe:0", arguments);
        Assert.Equal(payload, await File.ReadAllBytesAsync(stdinPath));

        await session.StopAsync(errorMessage: null);
    }

    private string StartLogPath => Path.Combine(_directory, "starts.log");

    private StreamSession CreateSession(
        string scriptPath,
        Action? onTerminated,
        StreamSource? source = null,
        IStreamInput? input = null)
    {
        var options = new StreamOptions(
            scriptPath,
            MaxStreams: 9,
            MaxViewersPerStream: 8,
            StartupTimeout: TimeSpan.FromSeconds(5),
            IdleTimeout: TimeSpan.FromSeconds(30),
            RtspTransport: "tcp");

        var credentials = CameraCredentials.FromConfiguration(new ConfigurationBuilder().Build());

        return new StreamSession(
            StreamKey.Live("camera", CameraStream.Sub),
            source ?? new RtspStreamSource("rtsp://fake.local/stream"),
            input,
            options,
            credentials,
            NullLogger.Instance,
            _ => onTerminated?.Invoke());
    }

    private string CreateScript(string body)
    {
        var path = Path.Combine(_directory, "ffmpeg.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static async Task WaitForFileLengthAsync(string path, int expectedLength)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(path))
            {
                var length = new FileInfo(path).Length;
                if (length >= expectedLength)
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException(
            $"Fake ffmpeg did not receive {expectedLength} bytes.");
    }

    private sealed class RecordingInput(byte[] payload) : IStreamInput
    {
        public async Task RunAsync(Stream destination, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(payload, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
    }

    private async Task<int> WaitForPidAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(StartLogPath))
            {
                var text = await File.ReadAllTextAsync(StartLogPath);
                if (int.TryParse(text.Trim(), out var pid))
                {
                    return pid;
                }
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("Fake ffmpeg did not start.");
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
