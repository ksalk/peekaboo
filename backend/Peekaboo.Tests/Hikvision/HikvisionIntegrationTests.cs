using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

/// <summary>
/// Opt-in checks against a real NVR. They are skipped unless
/// PEEKABOO_HIK_INTEGRATION=1 and the normal CAMERA_* / SDK path settings are set.
/// </summary>
public sealed class HikvisionIntegrationTests
{
    [Fact]
    public void Login_and_logout_against_a_real_nvr()
    {
        if (!IsEnabled())
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var options = HikvisionSdkOptions.FromConfiguration(configuration);
        using var runtime = new HikvisionSdkRuntime(
            options,
            NullLogger<HikvisionSdkRuntime>.Instance);
        var api = new HikvisionLoginApi(
            CameraCredentials.FromConfiguration(configuration),
            runtime,
            NullLogger<HikvisionLoginApi>.Instance);

        var host = Environment.GetEnvironmentVariable("HIK_TEST_NVR")
            ?? throw new InvalidOperationException("HIK_TEST_NVR is required.");
        var port = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_PORT"),
            out var parsedPort)
            ? parsedPort
            : 8000;

        var result = api.Login(host, port);

        try
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Device.SerialNumber));
            Assert.True(result.Device.IpChannelCount > 0);
            Assert.True(result.Device.StartDigitalChannel > 0);
        }
        finally
        {
            api.Logout(result.UserId);
        }
    }

    [Fact]
    public async Task Live_sdk_stream_delivers_ps_data()
    {
        if (!IsEnabled())
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var options = HikvisionSdkOptions.FromConfiguration(configuration);
        using var runtime = new HikvisionSdkRuntime(
            options,
            NullLogger<HikvisionSdkRuntime>.Instance);
        var loginApi = new HikvisionLoginApi(
            CameraCredentials.FromConfiguration(configuration),
            runtime,
            NullLogger<HikvisionLoginApi>.Instance);
        await using var pool = new HikvisionConnectionPool(
            loginApi,
            NullLogger<HikvisionConnectionPool>.Instance);

        var host = Environment.GetEnvironmentVariable("HIK_TEST_NVR")
            ?? throw new InvalidOperationException("HIK_TEST_NVR is required.");
        var port = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_PORT"),
            out var parsedPort)
            ? parsedPort
            : 8000;
        var isapiChannel = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_ISAPI_CHANNEL"),
            out var parsedChannel)
            ? parsedChannel
            : 1;
        var streamType = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_STREAM_TYPE"),
            out var parsedStreamType)
            ? parsedStreamType
            : 1;

        var input = new HikvisionLiveInput(
            new HikvisionLiveStreamSource(
                "integration",
                host,
                port,
                isapiChannel,
                streamType),
            pool,
            NullLogger<HikvisionLiveInput>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var destination = new MemoryStream();

        try
        {
            await Task.Run(
                () => input.RunAsync(destination, cancellation.Token),
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(
            destination.Length > 1000,
            $"Expected live SDK data, but received {destination.Length} bytes.");
    }

    [Fact]
    public async Task Playback_sdk_stream_delivers_ps_data()
    {
        if (!IsEnabled())
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var options = HikvisionSdkOptions.FromConfiguration(configuration);
        using var runtime = new HikvisionSdkRuntime(
            options,
            NullLogger<HikvisionSdkRuntime>.Instance);
        var loginApi = new HikvisionLoginApi(
            CameraCredentials.FromConfiguration(configuration),
            runtime,
            NullLogger<HikvisionLoginApi>.Instance);
        await using var pool = new HikvisionConnectionPool(
            loginApi,
            NullLogger<HikvisionConnectionPool>.Instance);

        var host = Environment.GetEnvironmentVariable("HIK_TEST_NVR")
            ?? throw new InvalidOperationException("HIK_TEST_NVR is required.");
        var port = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_PORT"),
            out var parsedPort)
            ? parsedPort
            : 8000;
        var isapiChannel = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_ISAPI_CHANNEL"),
            out var parsedChannel)
            ? parsedChannel
            : 1;
        var start = DateTimeOffset.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_PLAYBACK_START"),
            out var parsedStart)
            ? parsedStart
            : new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var seconds = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_PLAYBACK_SECONDS"),
            out var parsedSeconds)
            ? parsedSeconds
            : 5;

        var input = new HikvisionPlaybackInput(
            new HikvisionPlaybackStreamSource(
                "integration",
                host,
                port,
                isapiChannel,
                start,
                start.AddSeconds(seconds),
                TestNvrTimeZones.Utc),
            pool,
            NullLogger<HikvisionPlaybackInput>.Instance);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var destination = new CountingStream();
        var completed = false;

        try
        {
            // Match StreamSession's execution: run the input on the thread pool,
            // not on xUnit's synchronization context.
            await Task.Run(
                () => input.RunAsync(destination, cancellation.Token),
                cancellation.Token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(
            destination.Length > 1000,
            $"Expected playback SDK data, but received {destination.Length} bytes.");
        Assert.True(
            completed,
            "Playback input did not complete via the SDK end marker before the timeout.");

        var head = Convert.ToHexString(destination.Head);
        Assert.Contains("000001BA", head, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recording_search_returns_segments()
    {
        if (!IsEnabled())
        {
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var options = HikvisionSdkOptions.FromConfiguration(configuration);
        using var runtime = new HikvisionSdkRuntime(
            options,
            NullLogger<HikvisionSdkRuntime>.Instance);
        var loginApi = new HikvisionLoginApi(
            CameraCredentials.FromConfiguration(configuration),
            runtime,
            NullLogger<HikvisionLoginApi>.Instance);
        await using var pool = new HikvisionConnectionPool(
            loginApi,
            NullLogger<HikvisionConnectionPool>.Instance);
        var search = new HikvisionRecordingSearch(
            pool,
            NullLogger<HikvisionRecordingSearch>.Instance);

        var host = Environment.GetEnvironmentVariable("HIK_TEST_NVR")
            ?? throw new InvalidOperationException("HIK_TEST_NVR is required.");
        var port = int.TryParse(
            Environment.GetEnvironmentVariable("HIK_TEST_PORT"),
            out var parsedPort)
            ? parsedPort
            : 8000;
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        var result = await search.SearchAsync(
            new HikvisionSearchRequest(
                "integration",
                host,
                port,
                "101",
                start,
                start.AddMinutes(5),
                TestNvrTimeZones.Utc),
            CancellationToken.None);

        Assert.NotEmpty(result.Segments);
        Assert.All(result.Segments, segment => Assert.True(segment.End > segment.Start));
        Assert.Contains(
            result.Segments,
            segment => segment.Start < start.AddMinutes(5) && segment.End > start);
    }

    private sealed class CountingStream : Stream
    {
        private const int HeadLimit = 4096;
        private readonly MemoryStream _head = new();
        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Interlocked.Read(ref _length);

        public byte[] Head
        {
            get
            {
                lock (_head)
                {
                    return _head.ToArray();
                }
            }
        }

        public override long Position
        {
            get => Interlocked.Read(ref _length);
            set => throw new NotSupportedException();
        }

        private void Record(ReadOnlySpan<byte> buffer)
        {
            Interlocked.Add(ref _length, buffer.Length);

            lock (_head)
            {
                var remaining = HeadLimit - (int)_head.Length;
                if (remaining > 0)
                {
                    _head.Write(buffer[..Math.Min(remaining, buffer.Length)]);
                }
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Record(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => Record(buffer);

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            Record(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Record(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }

    private static bool IsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PEEKABOO_HIK_INTEGRATION"),
            "1",
            StringComparison.Ordinal);
}
