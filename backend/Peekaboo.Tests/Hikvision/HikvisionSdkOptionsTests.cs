using Microsoft.Extensions.Configuration;
using Peekaboo.Api.Hikvision;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

public sealed class HikvisionSdkOptionsTests
{
    [Fact]
    public void Defaults_are_safe_and_the_sdk_is_disabled()
    {
        var options = HikvisionSdkOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.False(options.IsConfigured);
        Assert.Null(options.SdkPath);
        Assert.Equal(3000u, options.ConnectTimeoutMs);
        Assert.Equal(10_000u, options.ReconnectIntervalMs);
        Assert.Equal(0u, options.LogLevel);
        Assert.Null(options.LogDirectory);
    }

    [Fact]
    public void Configuration_values_are_read_and_trimmed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PEEKABOO_HIK_SDK_PATH"] = "  /opt/hikvision  ",
                ["PEEKABOO_HIK_SDK_CONNECT_TIMEOUT_MS"] = "5000",
                ["PEEKABOO_HIK_SDK_RECONNECT_MS"] = "15000",
                ["PEEKABOO_HIK_SDK_LOG_LEVEL"] = "2",
                ["PEEKABOO_HIK_SDK_LOG_DIR"] = "/data/hik-logs",
            })
            .Build();

        var options = HikvisionSdkOptions.FromConfiguration(configuration);

        Assert.True(options.IsConfigured);
        Assert.Equal("/opt/hikvision", options.SdkPath);
        Assert.Equal(5000u, options.ConnectTimeoutMs);
        Assert.Equal(15_000u, options.ReconnectIntervalMs);
        Assert.Equal(2u, options.LogLevel);
        Assert.Equal("/data/hik-logs", options.LogDirectory);
    }

    [Theory]
    [InlineData("PEEKABOO_HIK_SDK_CONNECT_TIMEOUT_MS", "0")]
    [InlineData("PEEKABOO_HIK_SDK_CONNECT_TIMEOUT_MS", "60001")]
    [InlineData("PEEKABOO_HIK_SDK_CONNECT_TIMEOUT_MS", "-1")]
    [InlineData("PEEKABOO_HIK_SDK_RECONNECT_MS", "0")]
    [InlineData("PEEKABOO_HIK_SDK_RECONNECT_MS", "300001")]
    [InlineData("PEEKABOO_HIK_SDK_LOG_LEVEL", "4")]
    [InlineData("PEEKABOO_HIK_SDK_LOG_LEVEL", "not-a-number")]
    public void Invalid_numeric_settings_fail_fast(string name, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [name] = value })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => HikvisionSdkOptions.FromConfiguration(configuration));
    }
}
