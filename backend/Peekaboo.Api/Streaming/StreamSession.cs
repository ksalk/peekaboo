using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace Peekaboo.Api.Streaming;

/// <summary>
/// Owns exactly one ffmpeg process per camera. The process starts with the first
/// WebSocket subscriber and is killed when the last one leaves, so no camera is ever
/// pulled in the background.
/// </summary>
internal sealed class StreamSession
{
    private const int MaximumStderrLines = 20;

    private readonly string _safeUrl;
    private readonly string _streamUrl;
    private readonly TimeSpan? _maximumDuration;
    private readonly bool _isPlayback;
    private readonly StreamOptions _options;
    private readonly CameraCredentials _credentials;
    private readonly ILogger _logger;
    private readonly Action<StreamSession> _onTerminated;
    private readonly object _subscriberGate = new();
    private readonly Dictionary<Guid, StreamSubscriber> _subscribers = new();
    private readonly TaskCompletionSource<StreamInfo> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stderrGate = new();
    private readonly Queue<string> _stderrLines = new();

    private Process? _process;
    private StreamInfo? _streamInfo;
    private bool _readyPublished;
    private int _started;
    private int _stopping;
    private int _failureReported;
    private int _ended;
    private string? _failureMessage;
    private long _lastDataTimestamp;

    public StreamSession(
        StreamKey key,
        string cameraUrl,
        TimeSpan? maximumDuration,
        bool isPlayback,
        StreamOptions options,
        CameraCredentials credentials,
        ILogger logger,
        Action<StreamSession> onTerminated)
    {
        Key = key;
        _safeUrl = cameraUrl;
        _streamUrl = credentials.Resolve(cameraUrl);
        _maximumDuration = maximumDuration;
        _isPlayback = isPlayback;
        _options = options;
        _credentials = credentials;
        _logger = logger;
        _onTerminated = onTerminated;
    }

    public StreamKey Key { get; }

    public string CameraId => Key.CameraId;

    public string? FailureMessage => Volatile.Read(ref _failureMessage);

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastDataTimestamp, Stopwatch.GetTimestamp());

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.FfmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in BuildArguments())
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("ffmpeg did not start.");
            }

            _process = process;

            // StopAsync may have run while the process was starting; never leave it behind.
            if (Volatile.Read(ref _stopping) != 0)
            {
                KillProcess();
                return;
            }

            _logger.LogInformation("Stream started for {StreamKey}", Key);

            _ = Task.Run(() => ReadOutputAsync(process));
            _ = Task.Run(() => DrainStderrAsync(process));
            _ = Task.Run(WatchdogAsync);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            ReportFailure($"Could not start ffmpeg: {exception.Message}");
        }
    }

    public StreamSubscriber? AddSubscriber(int maxViewers)
    {
        var subscriber = new StreamSubscriber();

        lock (_subscriberGate)
        {
            if (Volatile.Read(ref _failureReported) != 0 ||
                Volatile.Read(ref _stopping) != 0 ||
                _subscribers.Count >= maxViewers)
            {
                return null;
            }

            _subscribers.Add(subscriber.Id, subscriber);

            if (_readyPublished)
            {
                EnqueueReady(subscriber, _streamInfo!);
            }
        }

        return subscriber;
    }

    public int RemoveSubscriber(StreamSubscriber subscriber)
    {
        lock (_subscriberGate)
        {
            _subscribers.Remove(subscriber.Id);
            return _subscribers.Count;
        }
    }

    public async Task RunSubscriberAsync(
        StreamSubscriber subscriber,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ready.Task.WaitAsync(_options.StartupTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            await RejectAsync(socket, "Timed out waiting for the camera stream.", cancellationToken);
            return;
        }
        catch (StreamEndedException)
        {
            await SendEndedAsync(socket, cancellationToken);
            return;
        }
        catch (StreamStartException exception)
        {
            await RejectAsync(socket, exception.Message, cancellationToken);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Subscribers are cancelled by their own socket lifetime, not by the process
        // token: on stop/failure the channel still has to deliver the final error.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sender = SendLoopAsync(subscriber, socket, lifetime.Token);
        var receiver = ReceiveLoopAsync(socket, lifetime.Token);

        await Task.WhenAny(sender, receiver);
        lifetime.Cancel();
        await AwaitQuietlyAsync(sender);
        await AwaitQuietlyAsync(receiver);

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            StreamWebSocket.Abort(socket);
        }
    }

    public async Task StopAsync(string? errorMessage)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        _cts.Cancel();

        lock (_subscriberGate)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                if (errorMessage is not null)
                {
                    subscriber.Messages.Writer.TryWrite(StreamMessage.Error(errorMessage));
                }

                subscriber.Messages.Writer.TryComplete();
            }
        }

        if (errorMessage is not null)
        {
            _ready.TrySetException(new StreamStartException(errorMessage));
        }
        else
        {
            _ready.TrySetCanceled();
        }

        KillProcess();

        var process = _process;
        if (process is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
            _process = null;
            _logger.LogInformation("Stream stopped for {StreamKey}", Key);
        }

        _onTerminated(this);
    }

    private string[] BuildArguments()
    {
        var durationArguments = _maximumDuration is { } duration
            ? new[]
            {
                "-t",
                duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            }
            : [];

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-rtsp_transport", _options.RtspTransport,
            "-analyzeduration", "1000000",
            "-probesize", "1000000",
            "-i", _streamUrl,
            "-map", "0:v:0",
            "-an",
            "-sn",
            "-dn",
            .. durationArguments,
            "-c:v", "copy",
            "-f", "mp4",
            "-movflags", "empty_moov+default_base_moof+frag_keyframe",
            "-flush_packets", "1",
            "pipe:1",
        ];
    }

    private async Task ReadOutputAsync(Process process)
    {
        var reader = new Fmp4Reader(process.StandardOutput.BaseStream);

        try
        {
            var initParts = new List<byte[]>();
            var initLength = 0;

            while (!_cts.IsCancellationRequested)
            {
                var box = await reader.ReadBoxAsync(_cts.Token);
                if (box is null)
                {
                    break;
                }

                Interlocked.Exchange(ref _lastDataTimestamp, Stopwatch.GetTimestamp());

                if (!_readyPublished)
                {
                    initParts.Add(box.Data);
                    initLength += box.Data.Length;

                    if (box.Type != "moov")
                    {
                        continue;
                    }

                    var codec = Fmp4Codec.TryGetAvc1Codec(box.Data);
                    if (codec is null)
                    {
                        ReportFailure(
                            _isPlayback
                                ? "The recording is not H.264. Peekaboo can only play H.264 recordings."
                                : "The camera stream is not H.264. Use the camera's H.264 substream.");
                        KillProcess();
                        break;
                    }

                    var initSegment = new byte[initLength];
                    var offset = 0;
                    foreach (var part in initParts)
                    {
                        Buffer.BlockCopy(part, 0, initSegment, offset, part.Length);
                        offset += part.Length;
                    }

                    PublishReady(new StreamInfo(codec, initSegment));
                    continue;
                }

                Broadcast(box);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException)
        {
            if (Volatile.Read(ref _stopping) == 0 && Volatile.Read(ref _failureReported) == 0)
            {
                ReportFailure($"Camera stream error: {exception.Message}");
            }
        }
        finally
        {
            if (Volatile.Read(ref _stopping) == 0)
            {
                await ReportUnexpectedExitAsync(process);
                KillProcess();
            }
        }
    }

    private async Task DrainStderrAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync(_cts.Token) is { } line)
            {
                lock (_stderrGate)
                {
                    _stderrLines.Enqueue(line);
                    while (_stderrLines.Count > MaximumStderrLines)
                    {
                        _stderrLines.Dequeue();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WatchdogAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                var idleTimeout = _isPlayback
                    ? TimeSpan.FromMinutes(2)
                    : _options.IdleTimeout;

                var idle = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastDataTimestamp));
                if (idle <= idleTimeout)
                {
                    continue;
                }

                var source = _isPlayback ? "recording" : "camera";
                ReportFailure(
                    $"No video data received from the {source} for " +
                    $"{idleTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds.");
                KillProcess();
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PublishReady(StreamInfo info)
    {
        lock (_subscriberGate)
        {
            _streamInfo = info;
            _readyPublished = true;

            foreach (var subscriber in _subscribers.Values)
            {
                EnqueueReady(subscriber, info);
            }

            _ready.TrySetResult(info);
        }
    }

    private void Broadcast(Fmp4Box box)
    {
        List<StreamSubscriber>? slowSubscribers = null;

        lock (_subscriberGate)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                if (subscriber.NeedsKeyframe)
                {
                    if (box.Type != "moof")
                    {
                        continue;
                    }

                    subscriber.NeedsKeyframe = false;
                }

                if (!subscriber.Messages.Writer.TryWrite(StreamMessage.Data(box.Data)))
                {
                    (slowSubscribers ??= []).Add(subscriber);
                }
            }
        }

        if (slowSubscribers is null)
        {
            return;
        }

        foreach (var subscriber in slowSubscribers)
        {
            subscriber.Messages.Writer.TryComplete(new SlowConsumerException());
        }
    }

    private static void EnqueueReady(StreamSubscriber subscriber, StreamInfo info)
    {
        subscriber.NeedsKeyframe = true;
        subscriber.Messages.Writer.TryWrite(StreamMessage.Ready(info.Codec));
        subscriber.Messages.Writer.TryWrite(StreamMessage.Init(info.InitSegment));
    }

    private static async Task SendLoopAsync(
        StreamSubscriber subscriber,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in subscriber.Messages.Reader.ReadAllAsync(cancellationToken))
            {
                switch (message.Kind)
                {
                    case StreamMessageKind.Ready:
                        await StreamWebSocket.SendJsonAsync(
                            socket,
                            new { type = "ready", codec = message.Text },
                            cancellationToken);
                        break;

                    case StreamMessageKind.Init:
                    case StreamMessageKind.Data:
                        await socket.SendAsync(
                            message.Payload!,
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            cancellationToken);
                        break;

                    case StreamMessageKind.Error:
                        await StreamWebSocket.SendJsonAsync(
                            socket,
                            new { type = "error", message = message.Text },
                            cancellationToken);
                        await StreamWebSocket.CloseOutputAsync(
                            socket,
                            WebSocketCloseStatus.PolicyViolation,
                            "Stream stopped",
                            cancellationToken);
                        return;

                    case StreamMessageKind.Ended:
                        await StreamWebSocket.SendJsonAsync(
                            socket,
                            new { type = "ended" },
                            cancellationToken);
                        await StreamWebSocket.CloseOutputAsync(
                            socket,
                            WebSocketCloseStatus.NormalClosure,
                            "Playback ended",
                            cancellationToken);
                        return;
                }
            }

            await StreamWebSocket.CloseOutputAsync(
                socket,
                WebSocketCloseStatus.NormalClosure,
                "Stream stopped",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (ChannelClosedException exception) when (exception.InnerException is SlowConsumerException)
        {
            try
            {
                await StreamWebSocket.SendJsonAsync(
                    socket,
                    new { type = "error", message = "Client is too slow; stream stopped." },
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }

            StreamWebSocket.Abort(socket);
        }
    }

    private static async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closed",
                            cancellationToken);
                    }

                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task SendEndedAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        try
        {
            await StreamWebSocket.SendJsonAsync(
                socket,
                new { type = "ended" },
                cancellationToken);
            await StreamWebSocket.CloseOutputAsync(
                socket,
                WebSocketCloseStatus.NormalClosure,
                "Playback ended",
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task RejectAsync(
        WebSocket socket,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await StreamWebSocket.SendJsonAsync(
                socket,
                new { type = "error", message },
                cancellationToken);
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

    private async Task ReportUnexpectedExitAsync(Process process)
    {
        if (Volatile.Read(ref _failureReported) != 0)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        int? exitCode = null;
        try
        {
            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
        }
        catch (InvalidOperationException)
        {
        }

        if (_isPlayback && exitCode == 0)
        {
            ReportEnded();
            return;
        }

        var detail = LastStderrDetail();
        var message = exitCode is null or 0
            ? "Camera stream ended."
            : $"Camera stream failed (ffmpeg exit code {exitCode}).";

        if (!string.IsNullOrWhiteSpace(detail))
        {
            message = $"{message} {detail}";
        }

        ReportFailure(message);
    }

    private string? LastStderrDetail()
    {
        lock (_stderrGate)
        {
            if (_stderrLines.Count == 0)
            {
                return null;
            }

            var line = _stderrLines.Last();
            return _credentials.Redact(line, _safeUrl);
        }
    }

    private void ReportEnded()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _failureReported, 1);
        _cts.Cancel();
        _logger.LogInformation("Playback ended for {StreamKey}", Key);

        lock (_subscriberGate)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Messages.Writer.TryWrite(StreamMessage.Ended());
                subscriber.Messages.Writer.TryComplete();
            }

            _ready.TrySetException(new StreamEndedException());
        }

        _onTerminated(this);
    }

    private void ReportFailure(string message)
    {
        if (Volatile.Read(ref _ended) != 0 ||
            Interlocked.Exchange(ref _failureReported, 1) != 0)
        {
            return;
        }

        var safeMessage = _credentials.Redact(message, _safeUrl);
        Volatile.Write(ref _failureMessage, safeMessage);
        _logger.LogWarning("Stream {StreamKey} failed: {Message}", Key, safeMessage);

        lock (_subscriberGate)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Messages.Writer.TryWrite(StreamMessage.Error(safeMessage));
                subscriber.Messages.Writer.TryComplete();
            }

            _ready.TrySetException(new StreamStartException(safeMessage));
        }

        _onTerminated(this);
    }

    private void KillProcess()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static async Task AwaitQuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // Both socket loops handle their own errors; this only guarantees cleanup.
        }
    }
}
