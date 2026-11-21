using System.Net.WebSockets;
using Peekaboo.Api.Cameras;

namespace Peekaboo.Api.Streaming;

/// <summary>
/// Identifies a stream session. <see cref="Kind"/> must be one of the canonical
/// <see cref="StreamKinds"/> values (or a playback kind); keys are compared by
/// exact string equality so sharing and the stream limit stay consistent.
/// </summary>
public readonly record struct StreamKey(string CameraId, string Kind)
{
    public static StreamKey Live(string cameraId, CameraStream stream) =>
        new(
            cameraId,
            stream == CameraStream.Main ? StreamKinds.Main : StreamKinds.Sub);

    public static StreamKey Playback(
        string cameraId,
        DateTimeOffset start,
        DateTimeOffset end) =>
        new(
            cameraId,
            $"{StreamKinds.PlaybackPrefix}{start.UtcTicks}-{end.UtcTicks}");

    public bool IsPlayback => Kind.StartsWith(StreamKinds.PlaybackPrefix, StringComparison.Ordinal);

    public override string ToString() => string.Concat(CameraId, ":", Kind);
}

public sealed class StreamManager(
    StreamOptions options,
    CameraCredentials credentials,
    IStreamInputFactory inputFactory,
    ILogger<StreamManager> logger) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<StreamKey, StreamSession> _sessions = new();
    private readonly Dictionary<StreamKey, Task> _stopping = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>
        _playbackGates = new(StringComparer.Ordinal);

    public async Task RunAsync(
        StreamKey key,
        Func<StreamSource?> resolveSource,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        StreamSession? session = null;
        StreamSubscriber? subscriber;
        string? rejection = null;
        var startedHere = false;

        SemaphoreSlim? playbackGate = null;
        if (key.IsPlayback)
        {
            playbackGate = _playbackGates.GetOrAdd(
                key.CameraId,
                static _ => new SemaphoreSlim(1, 1));
            await playbackGate.WaitAsync(cancellationToken);
        }

        try
        {
            if (key.IsPlayback)
            {
                await StopPlaybackSessionsAsync(key);
            }

            lock (_gate)
            {
                // Resolve the current source while holding the lock. This serializes
                // stream creation with update/delete stop scans, so a session can never
                // start with a configuration that was already replaced.
                StreamSource? source = null;
                try
                {
                    source = resolveSource();
                }
                catch (Exception exception)
                {
                    rejection = credentials.Redact(exception.Message, string.Empty);
                }

                if (rejection is null)
                {
                    if (source is null)
                    {
                        session = null;
                        rejection = "The camera was deleted.";
                    }
                    else if (!_sessions.TryGetValue(key, out session))
                    {
                        if (_sessions.Count >= options.MaxStreams)
                        {
                            session = null;
                            rejection = $"Stream limit reached ({options.MaxStreams} concurrent streams).";
                        }
                        else
                        {
                            IStreamInput? input = null;
                            try
                            {
                                input = inputFactory.Create(source);
                            }
                            catch (Exception exception)
                            {
                                rejection = credentials.Redact(exception.Message, string.Empty);
                            }

                            if (rejection is null)
                            {
                                session = new StreamSession(
                                    key,
                                    source,
                                    input,
                                    options,
                                    credentials,
                                    logger,
                                    OnSessionTerminated);
                                _sessions.Add(key, session);
                                startedHere = true;
                            }
                        }
                    }
                }

                subscriber = session?.AddSubscriber(options.MaxViewersPerStream);

                if (session is not null && subscriber is null)
                {
                    rejection = session.FailureMessage
                        ?? "Too many viewers for this camera, or the stream is stopping.";
                }
            }

            if (session is null || subscriber is null)
            {
                await RejectAsync(socket, rejection ?? "Stream rejected.", cancellationToken);
                return;
            }

            if (startedHere)
            {
                session.Start();
            }
        }
        finally
        {
            playbackGate?.Release();
        }

        try
        {
            await session.RunSubscriberAsync(subscriber, socket, cancellationToken);
        }
        finally
        {
            var isLastSubscriber = false;
            TaskCompletionSource? stopCompletion = null;

            lock (_gate)
            {
                isLastSubscriber = session.RemoveSubscriber(subscriber) == 0;

                if (isLastSubscriber &&
                    _sessions.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, session))
                {
                    _sessions.Remove(key);
                    stopCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _stopping[key] = stopCompletion.Task;
                }
            }

            if (isLastSubscriber)
            {
                try
                {
                    await session.StopAsync(errorMessage: null);
                }
                finally
                {
                    if (stopCompletion is not null)
                    {
                        stopCompletion.TrySetResult();

                        lock (_gate)
                        {
                            if (_stopping.TryGetValue(key, out var task) &&
                                ReferenceEquals(task, stopCompletion.Task))
                            {
                                _stopping.Remove(key);
                            }
                        }
                    }
                }
            }
        }
    }

    private async Task StopPlaybackSessionsAsync(StreamKey requestedKey)
    {
        StreamSession[] sessions;
        Task[] pendingStops;

        lock (_gate)
        {
            var keys = _sessions.Keys
                .Where(key =>
                    key.CameraId == requestedKey.CameraId &&
                    key.IsPlayback &&
                    key != requestedKey)
                .ToArray();
            sessions = keys.Select(key => _sessions[key]).ToArray();
            pendingStops = _stopping
                .Where(pair =>
                    pair.Key.CameraId == requestedKey.CameraId &&
                    pair.Key.IsPlayback)
                .Select(pair => pair.Value)
                .ToArray();

            foreach (var key in keys)
            {
                _sessions.Remove(key);
            }
        }

        var tasks = pendingStops
            .Concat(sessions.Select(session =>
                session.StopAsync("A new playback was requested.")))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    public Task StopCameraAsync(string cameraId, string message) =>
        StopCamerasAsync([cameraId], message);

    public async Task StopCamerasAsync(IEnumerable<string> cameraIds, string message)
    {
        var ids = cameraIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return;
        }

        var sessions = new List<StreamSession>();
        var pendingStops = new List<Task>();

        lock (_gate)
        {
            foreach (var key in _sessions.Keys.Where(key => ids.Contains(key.CameraId)).ToArray())
            {
                sessions.Add(_sessions[key]);
                _sessions.Remove(key);
            }

            foreach (var pair in _stopping.Where(pair => ids.Contains(pair.Key.CameraId)))
            {
                pendingStops.Add(pair.Value);
            }
        }

        await Task.WhenAll(
            pendingStops.Concat(sessions.Select(session => session.StopAsync(message))));
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
            if (_sessions.TryGetValue(session.Key, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(session.Key);
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
