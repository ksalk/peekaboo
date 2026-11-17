using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Peekaboo.Api.Streaming;
using Xunit;

namespace Peekaboo.Tests.Streaming;

public sealed class Fmp4Tests
{
    [Fact]
    public async Task Reader_returns_complete_boxes()
    {
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom0000"));
        var mdat = Box("mdat", new byte[] { 1, 2, 3, 4, 5 });
        var stream = new MemoryStream([.. ftyp, .. mdat]);
        var reader = new Fmp4Reader(stream);

        var first = await reader.ReadBoxAsync(CancellationToken.None);
        var second = await reader.ReadBoxAsync(CancellationToken.None);
        var end = await reader.ReadBoxAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal("ftyp", first.Type);
        Assert.Equal(ftyp, first.Data);
        Assert.NotNull(second);
        Assert.Equal("mdat", second.Type);
        Assert.Equal(mdat, second.Data);
        Assert.Null(end);
    }

    [Fact]
    public async Task Reader_supports_64_bit_box_sizes()
    {
        var large = new byte[40];
        BinaryPrimitives.WriteUInt32BigEndian(large, 1);
        Encoding.ASCII.GetBytes("mdat").CopyTo(large, 4);
        BinaryPrimitives.WriteUInt64BigEndian(large.AsSpan(8), (ulong)large.Length);

        var reader = new Fmp4Reader(new MemoryStream(large));

        var box = await reader.ReadBoxAsync(CancellationToken.None);

        Assert.NotNull(box);
        Assert.Equal("mdat", box.Type);
        Assert.Equal(large, box.Data);
    }

    [Fact]
    public async Task Reader_rejects_truncated_payload()
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, 16);
        Encoding.ASCII.GetBytes("moov").CopyTo(header, 4);

        var reader = new Fmp4Reader(new MemoryStream(header, writable: false));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => reader.ReadBoxAsync(CancellationToken.None));
    }

    [Fact]
    public void Codec_is_extracted_from_avcC()
    {
        var avcC = Box("avcC", [1, 0x64, 0x00, 0x1f, 0xff, 0xe1, 0x00, 0x00]);
        var moov = Box("moov", [.. Box("mvhd", new byte[8]), .. avcC]);

        Assert.Equal("avc1.64001f", Fmp4Codec.TryGetAvc1Codec(moov));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void Missing_avcC_yields_no_codec(byte[] moovPayload)
    {
        Assert.Null(Fmp4Codec.TryGetAvc1Codec(Box("moov", moovPayload)));
    }

    [Fact]
    public async Task Reader_parses_real_ffmpeg_fragmented_mp4_output()
    {
        Process process;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in new[]
                     {
                         "-hide_banner", "-loglevel", "error",
                         "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10",
                         "-t", "1", "-c:v", "libx264", "-preset", "ultrafast",
                         "-g", "5", "-pix_fmt", "yuv420p",
                         "-f", "mp4", "-movflags", "empty_moov+default_base_moof+frag_keyframe",
                         "pipe:1",
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("ffmpeg did not start.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return; // ffmpeg is not installed in this environment.
        }

        using (process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var reader = new Fmp4Reader(process.StandardOutput.BaseStream);

            string? codec = null;
            var sawMoof = false;
            var sawMdat = false;

            try
            {
                while (codec is null || !sawMoof || !sawMdat)
                {
                    var box = await reader.ReadBoxAsync(timeout.Token);
                    if (box is null)
                    {
                        break;
                    }

                    if (codec is null)
                    {
                        if (box.Type == "moov")
                        {
                            codec = Fmp4Codec.TryGetAvc1Codec(box.Data);
                        }

                        continue;
                    }

                    sawMoof |= box.Type == "moof";
                    sawMdat |= box.Type == "mdat";
                }
            }
            finally
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }

            Assert.NotNull(codec);
            Assert.StartsWith("avc1.", codec, StringComparison.Ordinal);
            Assert.True(sawMoof, "expected a moof box from ffmpeg");
            Assert.True(sawMdat, "expected an mdat box from ffmpeg");
        }
    }

    [Fact]
    public void Malformed_camera_configuration_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CAMERA_USERNAME"] = "camera",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => CameraCredentials.FromConfiguration(configuration));
    }

    [Fact]
    public void Camera_credentials_are_injected_and_redacted()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CAMERA_USERNAME"] = "cam user",
                ["CAMERA_PASSWORD"] = "p@ss:word",
            })
            .Build();

        var credentials = CameraCredentials.FromConfiguration(configuration);
        var resolved = credentials.Resolve("rtsp://camera.local:554/stream");

        Assert.Equal("rtsp://cam%20user:p%40ss%3Aword@camera.local:554/stream", resolved);

        var noCredentials = CameraCredentials.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal("rtsp://camera.local:554/stream", noCredentials.Resolve("rtsp://camera.local:554/stream"));

        var redacted = credentials.Redact(
            $"failed to open {resolved} with p@ss:word",
            "rtsp://camera.local:554/stream");

        Assert.DoesNotContain("p@ss:word", redacted, StringComparison.Ordinal);
        Assert.Contains("rtsp://camera.local:554/stream", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Stream_options_have_defaults_and_validation()
    {
        var defaults = StreamOptions.FromConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal(9, defaults.MaxStreams);
        Assert.Equal(8, defaults.MaxViewersPerStream);
        Assert.Equal("tcp", defaults.RtspTransport);
        Assert.Equal("ffmpeg", defaults.FfmpegPath);

        var invalid = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PEEKABOO_MAX_STREAMS"] = "0",
            })
            .Build();
        Assert.Throws<InvalidOperationException>(() => StreamOptions.FromConfiguration(invalid));

        var invalidViewers = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PEEKABOO_MAX_VIEWERS_PER_STREAM"] = "100",
            })
            .Build();
        Assert.Throws<InvalidOperationException>(() => StreamOptions.FromConfiguration(invalidViewers));

        var invalidTransport = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PEEKABOO_RTSP_TRANSPORT"] = "sctp",
            })
            .Build();
        Assert.Throws<InvalidOperationException>(() => StreamOptions.FromConfiguration(invalidTransport));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://camera.local", true)]
    [InlineData("https://camera.local", true)]
    [InlineData("http://camera.local:5000", false)]
    [InlineData("http://evil.example", false)]
    [InlineData("not an origin", false)]
    public void WebSocket_origin_must_match_the_request_host(string? origin, bool expected)
    {
        Assert.Equal(expected, StreamOrigin.IsAllowed(origin, new HostString("camera.local")));
    }

    [Theory]
    [InlineData("http://camera.local:8080", true)]
    [InlineData("http://camera.local:5000", false)]
    public void WebSocket_origin_must_match_an_explicit_request_port(string origin, bool expected)
    {
        Assert.Equal(
            expected,
            StreamOrigin.IsAllowed(origin, new HostString("camera.local:8080")));
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var data = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(data, 4);
        payload.CopyTo(data, 8);
        return data;
    }
}
