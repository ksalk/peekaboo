using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Data;
using Xunit;

namespace Peekaboo.Tests.Cameras;

public sealed class CameraServiceTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-camera-tests-").FullName;

    private readonly CameraService _cameras;

    public CameraServiceTests()
    {
        var database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));
        database.Initialize(NullLogger<AppDatabase>.Instance);
        _cameras = new CameraService(database);
    }

    [Fact]
    public void Create_list_update_and_delete_camera()
    {
        var created = _cameras.Create("Front door", "rtsp://192.168.1.10:554/stream1");

        var listed = Assert.Single(_cameras.List());
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Front door", listed.Name);

        var updated = _cameras.Update(
            created.Id,
            "Garage",
            "rtsp://192.168.1.11:554/stream2");

        Assert.NotNull(updated);
        Assert.Equal("Garage", updated.Name);
        Assert.Equal("rtsp://192.168.1.11:554/stream2", updated.Url);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        Assert.True(_cameras.Delete(created.Id));
        Assert.Empty(_cameras.List());
        Assert.False(_cameras.Delete(created.Id));
    }

    [Fact]
    public void Update_and_get_return_null_for_missing_camera()
    {
        Assert.Null(_cameras.Get("missing"));
        Assert.Null(_cameras.Update("missing", "Name", "rtsp://example.test/stream"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        Assert.NotNull(CameraValidation.NameError(name));
    }

    [Fact]
    public void Name_has_a_length_limit()
    {
        Assert.Null(CameraValidation.NameError(new string('a', 100)));
        Assert.NotNull(CameraValidation.NameError(new string('a', 101)));
    }

    [Theory]
    [InlineData("rtsp://192.168.1.10:554/stream", null)]
    [InlineData("rtsps://camera.local/stream", null)]
    [InlineData("RTSP://camera.local/stream", null)]
    [InlineData("http://camera.local/stream", "scheme")]
    [InlineData("rtsp://user:password@camera.local/stream", "credentials")]
    [InlineData("rtsp://camera.local/stream\nuser:pass@evil.test", "control")]
    [InlineData("not a url", "absolute")]
    [InlineData("", "required")]
    [InlineData(null, "required")]
    public void Url_validation_enforces_supported_scheme_and_rejects_credentials(
        string? url,
        string? expectedErrorFragment)
    {
        var error = CameraValidation.UrlError(url);

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
    public void Url_and_name_have_length_limits()
    {
        Assert.NotNull(CameraValidation.UrlError("rtsp://camera.local/" + new string('a', 2048)));
        Assert.NotNull(CameraValidation.NameError("camera\u0001name"));
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
