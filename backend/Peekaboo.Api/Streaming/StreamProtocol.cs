using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Peekaboo.Api.Streaming;

internal sealed record StreamInfo(string Codec, string? AudioCodec, byte[] InitSegment);

internal enum StreamMessageKind
{
    Ready,
    Init,
    Data,
    Error,
    Ended,
}

internal readonly record struct StreamMessage(
    StreamMessageKind Kind,
    string? Text = null,
    string? AudioCodec = null,
    byte[]? Payload = null)
{
    public static StreamMessage Ready(string codec, string? audioCodec) =>
        new(StreamMessageKind.Ready, Text: codec, AudioCodec: audioCodec);

    public static StreamMessage Init(byte[] data) => new(StreamMessageKind.Init, Payload: data);

    public static StreamMessage Data(byte[] data) => new(StreamMessageKind.Data, Payload: data);

    public static StreamMessage Error(string message) => new(StreamMessageKind.Error, Text: message);

    public static StreamMessage Ended() => new(StreamMessageKind.Ended);
}

internal sealed class SlowConsumerException : Exception;

internal sealed class StreamStartException(string message) : Exception(message);

internal sealed class StreamEndedException : Exception;

internal sealed class StreamSubscriber
{
    public Guid Id { get; } = Guid.NewGuid();

    public Channel<StreamMessage> Messages { get; } = Channel.CreateBounded<StreamMessage>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public bool NeedsKeyframe { get; set; } = true;
}

internal static class StreamWebSocket
{
    public static Task SendJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    public static async Task CloseOutputAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await socket.CloseOutputAsync(status, description, cancellationToken);
        }
        catch (WebSocketException)
        {
            // The peer is already gone.
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public static void Abort(WebSocket socket)
    {
        try
        {
            socket.Abort();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
