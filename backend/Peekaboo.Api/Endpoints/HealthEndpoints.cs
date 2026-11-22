using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Peekaboo.Api.Data;

namespace Peekaboo.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        var informational = typeof(HealthEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "unknown";
        }

        // The SDK appends +<commit> from git; keep /api/health on the release version.
        var buildMetadata = informational.IndexOf('+');
        return buildMetadata < 0 ? informational : informational[..buildMetadata];
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (AppDatabase database, CancellationToken cancellationToken) =>
        {
            await using var connection = database.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return Results.Ok(new { status = "ok", version = Version });
        }).AllowAnonymous();

        return app;
    }
}
