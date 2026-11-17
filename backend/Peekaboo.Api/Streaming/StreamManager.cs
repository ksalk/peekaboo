using System.Net.WebSockets;

namespace Peekaboo.Api.Streaming;

public sealed class StreamManager(
    StreamOptions options,
    CameraCredentials credentials,
    ILogger<StreamManager> logger) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StreamSession> _sessions = new();

    public async Task RunAsync(
        string cameraId,
        string cameraUrl,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        StreamSession? session;
        StreamSubscriber? subscriber;
        var startedHere = false;

        lock (_gate)
        {
            if (!_sessions.TryGetValue(cameraId, out session))
            {
                if (_sessions.Count >= options.MaxStreams)
                {
                    session = null;
                }
                else
                {
                    session = new StreamSession(
                        cameraId,
                        cameraUrl,
                        options,
                        credentials,
                        logger,
                        OnSessionTerminated);
                    _sessions.Add(cameraId, session);
                    startedHere = true;
                }
            }

            subscriber = session?.AddSubscriber(options.MaxViewersPerStream);
        }

        if (session is null || subscriber is null)
        {
            await RejectAsync(
                socket,
                session is null
                    ? $"Stream limit reached ({options.MaxStreams} concurrent streams)."
                    : "Too many viewers for this camera, or the stream is stopping.",
                cancellationToken);
            return;
        }

        try
        {
            if (startedHere)
            {
                session.Start();
            }

            await session.RunSubscriberAsync(subscriber, socket, cancellationToken);
        }
        finally
        {
            var isLastSubscriber = false;

            lock (_gate)
            {
                isLastSubscriber = session.RemoveSubscriber(subscriber) == 0;

                if (isLastSubscriber &&
                    _sessions.TryGetValue(cameraId, out var current) &&
                    ReferenceEquals(current, session))
                {
                    _sessions.Remove(cameraId);
                }
            }

            if (isLastSubscriber)
            {
                await session.StopAsync(errorMessage: null);
            }
        }
    }

    public async Task StopCameraAsync(string cameraId, string message)
    {
        StreamSession? session;

        lock (_gate)
        {
            _sessions.Remove(cameraId, out session);
        }

        if (session is not null)
        {
            await session.StopAsync(message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        StreamSession[] sessions;

        lock (_gate)
        {
            sessions = [.. _sessions.Values];
            _sessions.Clear();
        }

        await Task.WhenAll(sessions.Select(session => session.StopAsync("Server is shutting down.")));
    }

    private void OnSessionTerminated(StreamSession session)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(session.CameraId, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(session.CameraId);
            }
        }
    }

    private static async Task RejectAsync(
        WebSocket socket,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await StreamWebSocket.SendJsonAsync(socket, new { type = "error", message }, cancellationToken);
            await StreamWebSocket.CloseOutputAsync(
                socket,
                WebSocketCloseStatus.PolicyViolation,
                "Stream rejected",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }
}
