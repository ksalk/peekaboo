using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Peekaboo.Api.Isapi;

namespace Peekaboo.Tests.Api;

internal sealed class ApiTestHost : IDisposable
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "admin-password-123";

    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-api-tests-").FullName;

    private readonly WebApplicationFactory<Program> _factory;

    public ApiTestHost(
        IReadOnlyDictionary<string, string?>? settings = null,
        IIsapiClient? isapiClient = null,
        IIsapiAlarmLogClient? alarmLogClient = null)
    {
        DatabasePath = Path.Combine(_directory, "peekaboo.db");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PEEKABOO_DB_PATH", DatabasePath);
            builder.UseSetting("ADMIN_USERNAME", AdminUsername);
            builder.UseSetting("ADMIN_PASSWORD", AdminPassword);

            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }
            }

            if (isapiClient is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IIsapiClient>();
                    services.AddSingleton(isapiClient);
                });
            }

            if (alarmLogClient is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IIsapiAlarmLogClient>();
                    services.AddSingleton(alarmLogClient);
                });
            }
        });
    }

    public string DatabasePath { get; }

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
