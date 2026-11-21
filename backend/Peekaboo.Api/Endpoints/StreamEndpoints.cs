using Peekaboo.Api.Cameras;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Endpoints;

public static class StreamEndpoints
{
    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cameras/{id}/stream", HandleStreamAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task HandleStreamAsync(
        string id,
        string? stream,
        HttpContext context,
        CameraService cameras,
        StreamSourceFactory sourceFactory,
        StreamManager streams)
    {
        if (!StreamOrigin.IsAllowed(
                context.Request.Headers.Origin.ToString(),
                context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!TryParseStream(stream, out var cameraStream))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Stream must be 'main' or 'sub'.");
            return;
        }

        var target = cameras.GetStreamTarget(id, cameraStream);
        if (target is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        await streams.RunAsync(
            StreamKey.Live(target.CameraId, target.Stream),
            () =>
            {
                var current = cameras.GetStreamTarget(id, cameraStream);
                return current is null ? null : sourceFactory.CreateLive(current);
            },
            socket,
            context.RequestAborted);
    }

    private static bool TryParseStream(string? value, out CameraStream stream)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized) ||
            string.Equals(normalized, "sub", StringComparison.OrdinalIgnoreCase))
        {
            stream = CameraStream.Sub;
            return true;
        }

        if (string.Equals(normalized, "main", StringComparison.OrdinalIgnoreCase))
        {
            stream = CameraStream.Main;
            return true;
        }

        stream = default;
        return false;
    }
}
