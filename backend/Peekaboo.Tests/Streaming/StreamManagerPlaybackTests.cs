using System.Net.WebSockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class StreamManagerPlaybackTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-playback-manager-tests-").FullName;

    [Fact]
    public async Task A_new_playback_waits_for_the_previous_playback_to_stop()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var script = CreateScript("exec sleep 300");
        var options = new StreamOptions(
            script,
            MaxStreams: 9,
            MaxViewersPerStream: 8,
            StartupTimeout: TimeSpan.FromSeconds(30),
            IdleTimeout: TimeSpan.FromSeconds(30),
            RtspTransport: "tcp");
        var credentials = CameraCredentials.FromConfiguration(
            new ConfigurationBuilder().Build());
        var inputs = new BlockingInputFactory();

        await using var manager = new StreamManager(
            options,
            credentials,
            inputs,
            NullLogger<StreamManager>.Instance);

        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();

        var firstTask = manager.RunAsync(
            StreamKey.Playback("camera", start, start.AddMinutes(5)),
            () => new RtspStreamSource(
                "rtsp://fake.local/stream",
                Playback: true,
                Duration: TimeSpan.FromMinutes(5)),
            new SilentWebSocket(),
            firstCancellation.Token);

        var firstInput = await inputs.WaitForNextAsync();
        await firstInput.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondTask = manager.RunAsync(
            StreamKey.Playback("camera", start.AddMinutes(1), start.AddMinutes(6)),
            () => new RtspStreamSource(
                "rtsp://fake.local/stream",
                Playback: true,
                Duration: TimeSpan.FromMinutes(5)),
            new SilentWebSocket(),
            secondCancellation.Token);

        var secondInput = await inputs.WaitForNextAsync();
        await secondInput.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(
            firstInput.Stopped.Task.IsCompleted,
            "The new playback must not start before the previous one stops.");

        firstCancellation.Cancel();
        secondCancellation.Cancel();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(10));
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

    private sealed class BlockingInputFactory : IStreamInputFactory
    {
        private readonly SemaphoreSlim _next = new(0);
        private BlockingInput? _last;

        public IStreamInput? Create(StreamSource source)
        {
            _last = new BlockingInput();
            _next.Release();
            return _last;
        }

        public async Task<BlockingInput> WaitForNextAsync()
        {
            await _next.WaitAsync(TimeSpan.FromSeconds(10));
            return _last
                ?? throw new InvalidOperationException("The input factory was not called.");
        }
    }

    private sealed class BlockingInput : IStreamInput
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(Stream destination, CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                Stopped.TrySetResult();
            }
        }
    }

    private sealed class SilentWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new WebSocketReceiveResult(
                0,
                WebSocketMessageType.Close,
                endOfMessage: true);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
