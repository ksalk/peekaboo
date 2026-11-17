using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Peekaboo.Tests.Api;

public sealed class AuthenticationApiTests
{
    private sealed record UserResponse(string Id, string Username, bool IsAdmin);

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_unauthorized_without_session()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_and_logout_flow_works()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiTestHost.AdminUsername, password = ApiTestHost.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Equal(ApiTestHost.AdminUsername, me.Username);
        Assert.True(me.IsAdmin);

        using var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var afterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Login_rejects_wrong_password_and_missing_fields()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiTestHost.AdminUsername, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        using var missing = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = string.Empty, password = string.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Non_admins_cannot_manage_users()
    {
        using var host = new ApiTestHost();
        using var admin = await host.CreateAdminClientAsync();

        using var create = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "viewer", password = "viewer-password-123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var viewer = await host.CreateAuthenticatedClientAsync("viewer", "viewer-password-123");
        using var viewerList = await viewer.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, viewerList.StatusCode);

        using var anonymous = host.CreateClient();
        using var anonymousList = await anonymous.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_list_and_delete_users()
    {
        using var host = new ApiTestHost();
        using var admin = await host.CreateAdminClientAsync();

        using var create = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "garage", password = "garage-password-123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(created);

        using var duplicate = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "GARAGE", password = "another-password-123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var users = await admin.GetFromJsonAsync<List<UserResponse>>("/api/users");
        Assert.NotNull(users);
        Assert.Contains(users, user => user.Username == "garage");
        Assert.Contains(users, user => user.Username == ApiTestHost.AdminUsername);

        using var delete = await admin.DeleteAsync($"/api/users/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var deleteAgain = await admin.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_and_self_deletion_are_rejected()
    {
        using var host = new ApiTestHost();
        using var admin = await host.CreateAdminClientAsync();

        using var weak = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "short", password = "short", isAdmin = false });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var me = await admin.GetFromJsonAsync<UserResponse>("/api/auth/me");
        using var deleteSelf = await admin.DeleteAsync($"/api/users/{me!.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteSelf.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_after_repeated_failures()
    {
        using var host = new ApiTestHost();
        using var client = host.CreateClient();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username = ApiTestHost.AdminUsername, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiTestHost.AdminUsername, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("300", limited.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task Account_password_can_be_changed()
    {
        using var host = new ApiTestHost();
        using var admin = await host.CreateAdminClientAsync();

        using var change = await admin.PostAsJsonAsync(
            "/api/auth/password",
            new { currentPassword = ApiTestHost.AdminPassword, newPassword = "changed-password-456" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using var stillSignedIn = await admin.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);

        using var freshClient = host.CreateClient();
        using var login = await freshClient.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiTestHost.AdminUsername, password = "changed-password-456" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Admin_password_reset_revokes_target_sessions_but_not_own_session()
    {
        using var host = new ApiTestHost();
        using var admin = await host.CreateAdminClientAsync();

        using var create = await admin.PostAsJsonAsync(
            "/api/users",
            new { username = "viewer", password = "viewer-password-123", isAdmin = false });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var viewer = await host.CreateAuthenticatedClientAsync("viewer", "viewer-password-123");
        using var viewerBefore = await viewer.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, viewerBefore.StatusCode);

        var users = await admin.GetFromJsonAsync<List<UserResponse>>("/api/users");
        var viewerRecord = Assert.Single(users!, user => user.Username == "viewer");

        using var resetViewer = await admin.PutAsJsonAsync(
            $"/api/users/{viewerRecord.Id}/password",
            new { newPassword = "viewer-reset-password-456" });
        Assert.Equal(HttpStatusCode.NoContent, resetViewer.StatusCode);

        using var viewerAfter = await viewer.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, viewerAfter.StatusCode);

        using var relogin = host.CreateClient();
        using var reloginResponse = await relogin.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "viewer", password = "viewer-reset-password-456" });
        Assert.Equal(HttpStatusCode.OK, reloginResponse.StatusCode);

        var adminMe = await admin.GetFromJsonAsync<UserResponse>("/api/auth/me");
        using var resetSelf = await admin.PutAsJsonAsync(
            $"/api/users/{adminMe!.Id}/password",
            new { newPassword = "admin-new-password-456" });
        Assert.Equal(HttpStatusCode.NoContent, resetSelf.StatusCode);

        using var adminAfter = await admin.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, adminAfter.StatusCode);
    }
}
