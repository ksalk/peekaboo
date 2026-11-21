using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class StreamManagerTests
{
    [Fact]
    public async Task Run_rejects_when_the_resolved_source_no_longer_exists()
    {
        var options = new StreamOptions(
            FfmpegPath: "ffmpeg",
            MaxStreams: 9,
            MaxViewersPerStream: 8,
            StartupTimeout: TimeSpan.FromSeconds(5),
            IdleTimeout: TimeSpan.FromSeconds(30),
            RtspTransport: "tcp");

        var credentials = CameraCredentials.FromConfiguration(
            new ConfigurationBuilder().Build());

        await using var manager = new StreamManager(
            options,
            credentials,
            new NoopInputFactory(),
            NullLogger<StreamManager>.Instance);

        using var socket = new CapturingWebSocket();

        await manager.RunAsync(
            StreamKey.Live("camera-1", CameraStream.Sub),
            () => null,
            socket,
            CancellationToken.None);

        var message = Assert.Single(socket.TextMessages);
        Assert.Contains("\"type\":\"error\"", message, StringComparison.Ordinal);
        Assert.Contains("camera was deleted", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WebSocketState.CloseSent, socket.State);
    }

    [Fact]
    public async Task Run_rejects_when_source_resolution_throws()
    {
        var options = new StreamOptions(
            FfmpegPath: "ffmpeg",
            MaxStreams: 9,
            MaxViewersPerStream: 8,
            StartupTimeout: TimeSpan.FromSeconds(5),
            IdleTimeout: TimeSpan.FromSeconds(30),
            RtspTransport: "tcp");

        var credentials = CameraCredentials.FromConfiguration(
            new ConfigurationBuilder().Build());

        await using var manager = new StreamManager(
            options,
            credentials,
            new NoopInputFactory(),
            NullLogger<StreamManager>.Instance);

        using var socket = new CapturingWebSocket();

        await manager.RunAsync(
            StreamKey.Live("camera-1", CameraStream.Sub),
            () => throw new InvalidOperationException("Invalid SDK stream ID."),
            socket,
            CancellationToken.None);

        var message = Assert.Single(socket.TextMessages);
        Assert.Contains("Invalid SDK stream ID", message, StringComparison.Ordinal);
        Assert.Equal(WebSocketState.CloseSent, socket.State);
    }

    [Fact]
    public async Task Run_rejects_when_the_input_factory_throws()
    {
        var options = new StreamOptions(
            FfmpegPath: "ffmpeg",
            MaxStreams: 9,
            MaxViewersPerStream: 8,
            StartupTimeout: TimeSpan.FromSeconds(5),
            IdleTimeout: TimeSpan.FromSeconds(30),
            RtspTransport: "tcp");

        var credentials = CameraCredentials.FromConfiguration(
            new ConfigurationBuilder().Build());

        await using var manager = new StreamManager(
            options,
            credentials,
            new ThrowingInputFactory(),
            NullLogger<StreamManager>.Instance);

        using var socket = new CapturingWebSocket();

        await manager.RunAsync(
            StreamKey.Live("camera-1", CameraStream.Sub),
            () => new RtspStreamSource("rtsp://fake.local/stream"),
            socket,
            CancellationToken.None);

        var message = Assert.Single(socket.TextMessages);
        Assert.Contains("SDK input unavailable", message, StringComparison.Ordinal);
        Assert.Equal(WebSocketState.CloseSent, socket.State);
    }

    private sealed class NoopInputFactory : IStreamInputFactory
    {
        public IStreamInput? Create(StreamSource source) => null;
    }

    private sealed class ThrowingInputFactory : IStreamInputFactory
    {
        public IStreamInput? Create(StreamSource source) =>
            throw new InvalidOperationException("SDK input unavailable.");
    }

    private sealed class CapturingWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;

        public List<string> TextMessages { get; } = [];

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeStatusDescription;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(
                0,
                WebSocketMessageType.Close,
                endOfMessage: true));

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Record(buffer.AsMemory(), messageType);
            return Task.CompletedTask;
        }

        public override ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Record(buffer, messageType);
            return ValueTask.CompletedTask;
        }

        private void Record(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType)
        {
            if (messageType == WebSocketMessageType.Text)
            {
                TextMessages.Add(Encoding.UTF8.GetString(buffer.Span));
            }
        }
    }
}
