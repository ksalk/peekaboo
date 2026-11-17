using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class CameraApiTests
{
    private sealed record CameraResponse(
        string Id,
        string Name,
        string Url,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    [Fact]
    public async Task Camera_endpoints_require_authentication()
    {
        using var host = new ApiTestHost();
        using var anonymous = host.CreateClient();

        using var list = await anonymous.GetAsync("/api/cameras");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);

        using var create = await anonymous.PostAsJsonAsync(
            "/api/cameras",
            new { name = "Front door", url = "rtsp://camera.local/stream" });
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public async Task Authenticated_user_can_manage_cameras()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        using var create = await client.PostAsJsonAsync(
            "/api/cameras",
            new { name = "  Front door  ", url = "  rtsp://192.168.1.10:554/stream1  " });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<CameraResponse>();
        Assert.NotNull(created);
        Assert.Equal("Front door", created.Name);
        Assert.Equal("rtsp://192.168.1.10:554/stream1", created.Url);

        var fetched = await client.GetFromJsonAsync<CameraResponse>($"/api/cameras/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);

        using var missing = await client.GetAsync("/api/cameras/missing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var listed = await client.GetFromJsonAsync<List<CameraResponse>>("/api/cameras");
        Assert.NotNull(listed);
        var camera = Assert.Single(listed);
        Assert.Equal("Front door", camera.Name);

        using var update = await client.PutAsJsonAsync(
            $"/api/cameras/{created.Id}",
            new { name = "Garage", url = "rtsp://192.168.1.11:554/stream2" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await update.Content.ReadFromJsonAsync<CameraResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Garage", updated.Name);
        Assert.Equal("rtsp://192.168.1.11:554/stream2", updated.Url);

        using var delete = await client.DeleteAsync($"/api/cameras/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Empty((await client.GetFromJsonAsync<List<CameraResponse>>("/api/cameras"))!);

        using var deleteAgain = await client.DeleteAsync($"/api/cameras/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Non_admin_user_can_manage_cameras()
    {
        using var host = new ApiTestHost();
        using var admin = await host.CreateAdminClientAsync();

        using var createUser = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "viewer", password = "viewer-password-123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);

        using var viewer = await host.CreateAuthenticatedClientAsync("viewer", "viewer-password-123");
        using var createCamera = await viewer.PostAsJsonAsync(
            "/api/cameras",
            new { name = "Garden", url = "rtsp://192.168.1.12:554/stream" });

        Assert.Equal(HttpStatusCode.Created, createCamera.StatusCode);
    }

    [Theory]
    [InlineData("http://camera.local/stream", "Front door")]
    [InlineData("rtsp://user:password@camera.local/stream", "Front door")]
    [InlineData("rtsp://camera.local/stream", "")]
    public async Task Invalid_camera_payloads_are_rejected(string url, string name)
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/cameras",
            new { name, url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_of_missing_camera_returns_not_found()
    {
        using var host = new ApiTestHost();
        using var client = await host.CreateAdminClientAsync();

        using var response = await client.PutAsJsonAsync(
            "/api/cameras/missing",
            new { name = "Front door", url = "rtsp://camera.local/stream" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
