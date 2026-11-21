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

    private static bool IsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PEEKABOO_HIK_INTEGRATION"),
            "1",
            StringComparison.Ordinal);
}
