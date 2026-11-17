using Peekaboo.Api.Data;

namespace Peekaboo.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (AppDatabase database, CancellationToken cancellationToken) =>
        {
            await using var connection = database.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return Results.Ok(new { status = "ok" });
        });

        return app;
    }
}
