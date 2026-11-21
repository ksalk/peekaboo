using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Hikvision;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

public sealed class HikvisionSdkRuntimeTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-hik-runtime-tests-").FullName;

    [Fact]
    public void Missing_sdk_path_fails_with_actionable_message()
    {
        var runtime = CreateRuntime(sdkPath: null);

        var exception = Assert.Throws<HikvisionException>(runtime.EnsureInitialized);

        Assert.Equal(HikvisionFailureKind.Unavailable, exception.Kind);
        Assert.Contains("PEEKABOO_HIK_SDK_PATH", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_sdk_directory_fails_with_actionable_message()
    {
        var runtime = CreateRuntime(Path.Combine(_directory, "missing"));

        var exception = Assert.Throws<HikvisionException>(runtime.EnsureInitialized);

        Assert.Equal(HikvisionFailureKind.Unavailable, exception.Kind);
        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Incomplete_sdk_layout_fails_before_loading_the_library()
    {
        var sdkPath = Path.Combine(_directory, "empty");
        Directory.CreateDirectory(sdkPath);
        var runtime = CreateRuntime(sdkPath);

        var exception = Assert.Throws<HikvisionException>(runtime.EnsureInitialized);

        Assert.Equal(HikvisionFailureKind.Unavailable, exception.Kind);
        Assert.Contains("libhcnetsdk.so", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_is_idempotent_and_use_after_dispose_throws()
    {
        var runtime = CreateRuntime(sdkPath: null);

        runtime.Dispose();
        runtime.Dispose();

        Assert.Throws<ObjectDisposedException>(runtime.EnsureInitialized);
    }

    private static HikvisionSdkRuntime CreateRuntime(string? sdkPath) =>
        new(
            new HikvisionSdkOptions(
                sdkPath,
                ConnectTimeoutMs: 3000,
                ReconnectIntervalMs: 10_000,
                LogLevel: 0,
                LogDirectory: null),
            NullLogger<HikvisionSdkRuntime>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
