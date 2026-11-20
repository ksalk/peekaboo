using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Data;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Cameras;

public sealed class CameraServiceTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-camera-tests-").FullName;

    private readonly NvrService _nvrs;
    private readonly CameraService _cameras;

    public CameraServiceTests()
    {
        var database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));
        database.Initialize(NullLogger<AppDatabase>.Instance);
        _nvrs = new NvrService(database);
        _cameras = new CameraService(database);
    }

    [Fact]
    public void Create_list_update_and_delete_camera()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");
        var created = _cameras.Create(nvr.Id, "Front door", "101", "102");

        Assert.NotNull(created);
        var listed = Assert.Single(_cameras.List());
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(nvr.Id, listed.NvrId);
        Assert.Equal("House NVR", listed.NvrName);
        Assert.Equal("Front door", listed.Name);
        Assert.Equal("101", listed.MainStreamId);
        Assert.Equal("102", listed.SubStreamId);

        var updated = _cameras.Update(nvr.Id, created.Id, "Garage", "201", "202");

        Assert.NotNull(updated);
        Assert.Equal("Garage", updated.Name);
        Assert.Equal("201", updated.MainStreamId);
        Assert.Equal("202", updated.SubStreamId);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        Assert.True(_cameras.Delete(nvr.Id, created.Id));
        Assert.Empty(_cameras.List());
        Assert.False(_cameras.Delete(nvr.Id, created.Id));
    }

    [Fact]
    public void Camera_is_scoped_to_its_nvr()
    {
        var firstNvr = _nvrs.Create("First NVR", "rtsp://192.168.1.64:554");
        var secondNvr = _nvrs.Create("Second NVR", "rtsp://192.168.1.65:554");
        var camera = _cameras.Create(firstNvr.Id, "Front door", "101", "102");

        Assert.NotNull(camera);
        Assert.Null(_cameras.Update(secondNvr.Id, camera.Id, "Moved", "201", "202"));
        Assert.False(_cameras.Delete(secondNvr.Id, camera.Id));
        Assert.NotNull(_cameras.Get(camera.Id));
    }

    [Fact]
    public void Create_under_missing_nvr_returns_null()
    {
        Assert.Null(_cameras.Create("missing", "Front door", "101", "102"));
    }

    [Fact]
    public void Update_and_get_return_null_for_missing_camera()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");

        Assert.Null(_cameras.Get("missing"));
        Assert.Null(_cameras.Update(nvr.Id, "missing", "Name", "101", "102"));
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
    public void Name_has_a_length_limit_and_rejects_control_characters()
    {
        Assert.Null(CameraValidation.NameError(new string('a', 100)));
        Assert.NotNull(CameraValidation.NameError(new string('a', 101)));
        Assert.NotNull(CameraValidation.NameError("camera\u0001name"));
    }

    [Theory]
    [InlineData("101", null)]
    [InlineData(" 102 ", null)]
    [InlineData("1", null)]
    [InlineData("0", "positive")]
    [InlineData("-1", "positive")]
    [InlineData("abc", "positive")]
    [InlineData("10a", "positive")]
    [InlineData("1234567", "positive")]
    [InlineData("", "required")]
    [InlineData(null, "required")]
    public void Stream_id_validation_requires_a_positive_number(
        string? streamId,
        string? expectedErrorFragment)
    {
        var error = CameraValidation.StreamIdError(streamId, "Main stream ID");

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
    public void Stream_ids_are_normalized()
    {
        Assert.Equal("101", CameraValidation.NormalizeStreamId(" 0101 "));
        Assert.Equal("1", CameraValidation.NormalizeStreamId("0001"));
    }

    [Fact]
    public void Stream_target_uses_the_selected_isapi_stream_id()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");

        Assert.NotNull(camera);

        var main = _cameras.GetStreamTarget(camera.Id, CameraStream.Main);
        var sub = _cameras.GetStreamTarget(camera.Id, CameraStream.Sub);

        Assert.NotNull(main);
        Assert.NotNull(sub);
        Assert.Equal("rtsp://192.168.1.64:554/ISAPI/Streaming/channels/101", main.Url);
        Assert.Equal("rtsp://192.168.1.64:554/ISAPI/Streaming/channels/102", sub.Url);
        Assert.Null(_cameras.GetStreamTarget("missing", CameraStream.Sub));
    }

    [Fact]
    public void Playback_source_uses_the_camera_main_stream_and_nvr_base_url()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");
        var camera = _cameras.Create(nvr.Id, "Front door", "101", "102");

        Assert.NotNull(camera);

        var source = _cameras.GetPlaybackSource(camera.Id);

        Assert.NotNull(source);
        Assert.Equal(camera.Id, source.CameraId);
        Assert.Equal("Front door", source.CameraName);
        Assert.Equal(nvr.Id, source.NvrId);
        Assert.Equal("rtsp://192.168.1.64:554", source.RtspBaseUrl);
        Assert.Equal("101", source.MainStreamId);
        Assert.Null(_cameras.GetPlaybackSource("missing"));
    }

    [Fact]
    public void Camera_ids_can_be_listed_for_an_nvr()
    {
        var nvr = _nvrs.Create("House NVR", "rtsp://192.168.1.64:554");
        var first = _cameras.Create(nvr.Id, "Front door", "101", "102");
        var second = _cameras.Create(nvr.Id, "Garden", "201", "202");

        Assert.NotNull(first);
        Assert.NotNull(second);

        var ids = _cameras.ListCameraIdsForNvr(nvr.Id);

        Assert.Equal(2, ids.Count);
        Assert.Contains(first.Id, ids);
        Assert.Contains(second.Id, ids);
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
