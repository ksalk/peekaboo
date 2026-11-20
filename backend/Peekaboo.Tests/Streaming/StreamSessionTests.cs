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

    private string StartLogPath => Path.Combine(_directory, "starts.log");

    private StreamSession CreateSession(string scriptPath, Action? onTerminated)
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
            "rtsp://fake.local/stream",
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
