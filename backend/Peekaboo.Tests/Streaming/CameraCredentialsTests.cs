using Microsoft.Extensions.Configuration;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class CameraCredentialsTests
{
    private static CameraCredentials Create() =>
        CameraCredentials.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CAMERA_USERNAME"] = "viewer",
                ["CAMERA_PASSWORD"] = "s3cret",
            })
            .Build());

    [Fact]
    public void Resolve_injects_credentials_at_stream_start()
    {
        var resolved = Create().Resolve("rtsp://nvr.local/ISAPI/Streaming/channels/101");

        Assert.Equal(
            "rtsp://viewer:s3cret@nvr.local/ISAPI/Streaming/channels/101",
            resolved);
    }

    [Fact]
    public void Redact_removes_the_password_even_without_a_url()
    {
        var redacted = Create().Redact("failed with s3cret", string.Empty);

        Assert.Equal("failed with ***", redacted);
    }

    [Fact]
    public void Redact_removes_both_the_resolved_url_and_the_password()
    {
        var credentials = Create();
        var safeUrl = "rtsp://nvr.local/stream";
        var text = $"failed at {credentials.Resolve(safeUrl)} using s3cret";

        var redacted = credentials.Redact(text, safeUrl);

        Assert.DoesNotContain("s3cret", redacted, StringComparison.Ordinal);
        Assert.Contains(safeUrl, redacted, StringComparison.Ordinal);
    }
}
