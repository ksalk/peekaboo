using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Data;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Nvrs;

public sealed class NvrServiceTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-nvr-tests-").FullName;

    private readonly NvrService _nvrs;
    private readonly CameraService _cameras;

    public NvrServiceTests()
    {
        var database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));
        database.Initialize(NullLogger<AppDatabase>.Instance);
        _nvrs = new NvrService(database);
        _cameras = new CameraService(database);
    }

    [Fact]
    public void Create_list_update_and_delete_nvr()
    {
        var created = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");

        var listed = Assert.Single(_nvrs.List());
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("House NVR", listed.Name);
        Assert.Equal("rtsp://192.168.1.64:554", listed.BaseUrl);

        var updated = _nvrs.Update(created.Id, "Garage NVR", "rtsps://192.168.1.65:322");

        Assert.NotNull(updated);
        Assert.Equal("Garage NVR", updated.Name);
        Assert.Equal("rtsps://192.168.1.65:322", updated.BaseUrl);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        Assert.Empty(_nvrs.Delete(created.Id)!);
        Assert.Empty(_nvrs.List());
        Assert.Null(_nvrs.Delete(created.Id));
    }

    [Fact]
    public void Deleting_an_nvr_cascades_cameras_and_returns_their_ids()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");
        var first = _cameras.Create(nvr.Id, "Front door", "101", "102");
        var second = _cameras.Create(nvr.Id, "Garden", "201", "202");

        Assert.NotNull(first);
        Assert.NotNull(second);

        var cameraIds = _nvrs.Delete(nvr.Id);

        Assert.NotNull(cameraIds);
        Assert.Equal(2, cameraIds.Count);
        Assert.Contains(first.Id, cameraIds);
        Assert.Contains(second.Id, cameraIds);
        Assert.Null(_cameras.Get(first.Id));
        Assert.Null(_cameras.Get(second.Id));
    }

    [Fact]
    public void Update_and_get_return_null_for_missing_nvr()
    {
        Assert.Null(_nvrs.Get("missing"));
        Assert.Null(_nvrs.Update("missing", "Name", "rtsp://nvr.local:554"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        Assert.NotNull(NvrValidation.NameError(name));
    }

    [Fact]
    public void Name_has_a_length_limit_and_rejects_control_characters()
    {
        Assert.Null(NvrValidation.NameError(new string('a', 100)));
        Assert.NotNull(NvrValidation.NameError(new string('a', 101)));
        Assert.NotNull(NvrValidation.NameError("NVR\u0001name"));
    }

    [Theory]
    [InlineData("rtsp://192.168.1.64:554", null)]
    [InlineData("rtsps://nvr.local:322", null)]
    [InlineData("RTSP://nvr.local", null)]
    [InlineData("http://nvr.local", "scheme")]
    [InlineData("rtsp://user:password@nvr.local:554", "credentials")]
    [InlineData("rtsp://nvr.local:554/stream", "path")]
    [InlineData("rtsp://nvr.local:0", "port")]
    [InlineData("rtsp://nvr.local:554?channel=1", "query")]
    [InlineData("rtsp://nvr.local:554#fragment", "fragment")]
    [InlineData("rtsp://nvr.local:554\nuser:pass@evil.test", "control")]
    [InlineData("rtsp://nvr.local\u200b", "ascii")]
    [InlineData("not a url", "absolute")]
    [InlineData("", "required")]
    [InlineData(null, "required")]
    public void Base_url_validation_enforces_rtsp_host_only_urls(
        string? baseUrl,
        string? expectedErrorFragment)
    {
        var error = NvrValidation.BaseUrlError(baseUrl);

        if (expectedErrorFragment is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
            Assert.Contains(expectedErrorFragment, error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Base_url_has_a_length_limit_and_is_normalized()
    {
        Assert.NotNull(NvrValidation.BaseUrlError("rtsp://" + new string('a', 2042)));
        Assert.Equal(
            "rtsp://192.168.1.64:554",
            NvrValidation.NormalizeBaseUrl("  rtsp://192.168.1.64:554/  "));
        Assert.Equal(
            "rtsp://nvr.local",
            NvrValidation.NormalizeBaseUrl("RTSP://NVR.Local"));
        Assert.Equal(
            "rtsp://nvr.local",
            NvrValidation.NormalizeBaseUrl("rtsp://nvr.local/./"));
        Assert.Equal(
            "rtsp://[::1]:554",
            NvrValidation.NormalizeBaseUrl("rtsp://[::1]:554/"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
