using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;

namespace Peekaboo.Tests.Api;

internal sealed class ApiTestHost : IDisposable
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin-password-123";

    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-api-tests-").FullName;

    private readonly WebApplicationFactory<Program> _factory;

    public ApiTestHost(IReadOnlyDictionary<string, string?>? settings = null)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PEEKABOO_DB_PATH", Path.Combine(_directory, "peekaboo.db"));
            builder.UseSetting("ADMIN_USERNAME", AdminUsername);
            builder.UseSetting("ADMIN_PASSWORD", AdminPassword);

            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }
            }
        });
    }

    public TestServer Server => _factory.Server;

    public HttpClient CreateClient() => _factory.CreateClient();

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string username, string password)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Login for '{username}' failed with {(int)response.StatusCode}.");
        }

        return client;
    }

    public Task<HttpClient> CreateAdminClientAsync() =>
        CreateAuthenticatedClientAsync(AdminUsername, AdminPassword);

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
