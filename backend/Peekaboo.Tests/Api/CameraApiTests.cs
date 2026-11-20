using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class CameraApiTests
{
    private sealed record NvrResponse(
        string Id,
        string Name,
        string BaseUrl,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record CameraResponse(
        string Id,
        string NvrId,
        string NvrName,
        string Name,
        string MainStreamId,
        string SubStreamId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    [Fact]
    public async Task Nvr_and_camera_endpoints_require_authentication()
    {
        using var host = new ApiTestHost();
        using var anonymous = host.CreateClient();

        using var listNvrs = await anonymous.GetAsync("/api/nvrs");
        Assert.Equal(HttpStatusCode.Unauthorized, listNvrs.StatusCode);

        using var createNvr = await anonymous.PostAsJsonAsync(
            "/api/nvrs",
            new { name = "House NVR", baseUrl = "rtsp://192.168.1.64:554" });
        Assert.Equal(HttpStatusCode.Unauthorized, createNvr.StatusCode);

        using var listCameras = await anonymous.GetAsync("/api/cameras");
        Assert.Equal(HttpStatusCode.Unauthorized, listCameras.StatusCode);

        using var createCamera = await anonymous.PostAsJsonAsync(
            "/api/nvrs/missing/cameras",
            new { name = "Front door", mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.Unauthorized, createCamera.StatusCode);
    }

    [Fact]
    public async Task Authenticated_user_can_manage_nvrs_and_cameras()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        var nvr = await CreateNvrAsync(
            client,
            "  House NVR  ",
            "  rtsp://192.168.1.64:554/  ");

        Assert.Equal("House NVR", nvr.Name);
        Assert.Equal("rtsp://192.168.1.64:554", nvr.BaseUrl);

        var fetchedNvr = await client.GetFromJsonAsync<NvrResponse>($"/api/nvrs/{nvr.Id}");
        Assert.NotNull(fetchedNvr);
        Assert.Equal(nvr.Id, fetchedNvr.Id);

        using var missingNvr = await client.GetAsync("/api/nvrs/missing");
        Assert.Equal(HttpStatusCode.NotFound, missingNvr.StatusCode);

        var camera = await CreateCameraAsync(
            client,
            nvr.Id,
            "  Front door  ",
            " 101 ",
            " 0102 ");

        Assert.Equal(nvr.Id, camera.NvrId);
        Assert.Equal("House NVR", camera.NvrName);
        Assert.Equal("Front door", camera.Name);
        Assert.Equal("101", camera.MainStreamId);
        Assert.Equal("102", camera.SubStreamId);

        var fetchedCamera = await client.GetFromJsonAsync<CameraResponse>($"/api/cameras/{camera.Id}");
        Assert.NotNull(fetchedCamera);
        Assert.Equal(camera.Id, fetchedCamera.Id);

        var listedCameras = await client.GetFromJsonAsync<List<CameraResponse>>("/api/cameras");
        Assert.NotNull(listedCameras);
        var listed = Assert.Single(listedCameras);
        Assert.Equal("Front door", listed.Name);
        Assert.Equal("House NVR", listed.NvrName);

        using var update = await client.PutAsJsonAsync(
            $"/api/nvrs/{nvr.Id}/cameras/{camera.Id}",
            new { name = "Garage", mainStreamId = "201", subStreamId = "202" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await update.Content.ReadFromJsonAsync<CameraResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Garage", updated.Name);
        Assert.Equal("201", updated.MainStreamId);
        Assert.Equal("202", updated.SubStreamId);

        using var deleteCamera = await client.DeleteAsync(
            $"/api/nvrs/{nvr.Id}/cameras/{camera.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteCamera.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<CameraResponse>>("/api/cameras"))!);

        using var deleteNvr = await client.DeleteAsync($"/api/nvrs/{nvr.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteNvr.StatusCode);

        using var deleteAgain = await client.DeleteAsync($"/api/nvrs/{nvr.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_nvr_removes_its_cameras()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();
        var nvr = await CreateNvrAsync(client);
        await CreateCameraAsync(client, nvr.Id, "Front door", "101", "102");

        using var deleteNvr = await client.DeleteAsync($"/api/nvrs/{nvr.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteNvr.StatusCode);

        var cameras = await client.GetFromJsonAsync<List<CameraResponse>>("/api/cameras");
        Assert.NotNull(cameras);
        Assert.Empty(cameras);
    }

    [Fact]
    public async Task Non_admin_user_can_manage_nvrs_and_cameras()
    {
        using var host = new ApiTestHost();
        var admin = await host.CreateAdminClientAsync();

        using var createUser = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "viewer", password = "viewer-password-123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);

        using var viewer = await host.CreateAuthenticatedClientAsync("viewer", "viewer-password-123");

        var nvr = await CreateNvrAsync(viewer, "Viewer NVR", "rtsp://192.168.1.64:554");
        var camera = await CreateCameraAsync(viewer, nvr.Id, "Garden", "101", "102");

        Assert.Equal("Viewer NVR", camera.NvrName);
    }

    [Fact]
    public async Task Nvr_address_accepts_bare_hosts_and_normalizes_them()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        var nvr = await CreateNvrAsync(client, "Bare NVR", "  192.168.1.65  ");

        Assert.Equal("rtsp://192.168.1.65:554", nvr.BaseUrl);
    }

    [Theory]
    [InlineData("House NVR", "http://192.168.1.64", "rtsp")]
    [InlineData("House NVR", "rtsp://user:password@192.168.1.64:554", "credentials")]
    [InlineData("House NVR", "rtsp://192.168.1.64:554/stream", "path")]
    [InlineData("House NVR", "not a url", "absolute")]
    [InlineData("", "rtsp://192.168.1.64:554", "name")]
    public async Task Invalid_nvr_payloads_are_rejected(
        string name,
        string baseUrl,
        string expectedErrorFragment)
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/nvrs",
            new { name, baseUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedErrorFragment, body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Front door", "abc", "102", "main stream")]
    [InlineData("Front door", "101", "0", "sub stream")]
    [InlineData("Front door", "101", "1234567", "sub stream")]
    [InlineData("", "101", "102", "name")]
    public async Task Invalid_camera_payloads_are_rejected(
        string name,
        string mainStreamId,
        string subStreamId,
        string expectedErrorFragment)
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();
        var nvr = await CreateNvrAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/nvrs/{nvr.Id}/cameras",
            new { name, mainStreamId, subStreamId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedErrorFragment, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Camera_routes_are_scoped_to_the_nvr()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();
        var firstNvr = await CreateNvrAsync(client, "First NVR", "rtsp://192.168.1.64:554");
        var secondNvr = await CreateNvrAsync(client, "Second NVR", "rtsp://192.168.1.65:554");
        var camera = await CreateCameraAsync(client, firstNvr.Id, "Front door", "101", "102");

        using var update = await client.PutAsJsonAsync(
            $"/api/nvrs/{secondNvr.Id}/cameras/{camera.Id}",
            new { name = "Moved", mainStreamId = "201", subStreamId = "202" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        using var delete = await client.DeleteAsync(
            $"/api/nvrs/{secondNvr.Id}/cameras/{camera.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        Assert.NotNull(await client.GetFromJsonAsync<CameraResponse>($"/api/cameras/{camera.Id}"));
    }

    [Fact]
    public async Task Operations_on_missing_resources_return_not_found()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        using var missingNvrUpdate = await client.PutAsJsonAsync(
            "/api/nvrs/missing",
            new { name = "House NVR", baseUrl = "rtsp://192.168.1.64:554" });
        Assert.Equal(HttpStatusCode.NotFound, missingNvrUpdate.StatusCode);

        using var missingNvrCamera = await client.PostAsJsonAsync(
            "/api/nvrs/missing/cameras",
            new { name = "Front door", mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.NotFound, missingNvrCamera.StatusCode);

        using var missingCameraUpdate = await client.PutAsJsonAsync(
            "/api/nvrs/missing/cameras/missing",
            new { name = "Front door", mainStreamId = "101", subStreamId = "102" });
        Assert.Equal(HttpStatusCode.NotFound, missingCameraUpdate.StatusCode);

        using var missingCameraDelete = await client.DeleteAsync(
            "/api/nvrs/missing/cameras/missing");
        Assert.Equal(HttpStatusCode.NotFound, missingCameraDelete.StatusCode);
    }

    private static async Task<NvrResponse> CreateNvrAsync(
        HttpClient client,
        string name = "House NVR",
        string baseUrl = "rtsp://192.168.1.64:554")
    {
        using var response = await client.PostAsJsonAsync(
            "/api/nvrs",
            new { name, baseUrl });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var nvr = await response.Content.ReadFromJsonAsync<NvrResponse>();
        Assert.NotNull(nvr);
        return nvr;
    }

    private static async Task<CameraResponse> CreateCameraAsync(
        HttpClient client,
        string nvrId,
        string name,
        string mainStreamId,
        string subStreamId)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/nvrs/{nvrId}/cameras",
            new { name, mainStreamId, subStreamId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var camera = await response.Content.ReadFromJsonAsync<CameraResponse>();
        Assert.NotNull(camera);
        return camera;
    }
}
