using System.Net.WebSockets;
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
        HttpContext context,
        CameraService cameras,
        StreamManager streams)
    {
        if (!StreamOrigin.IsAllowed(
                context.Request.Headers.Origin.ToString(),
                context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.");
            return;
        }

        var camera = cameras.Get(id);
        if (camera is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();

        // The camera may have been deleted between the lookup and the upgrade.
        if (cameras.Get(camera.Id) is null)
        {
            try
            {
                await StreamWebSocket.SendJsonAsync(
                    socket,
                    new { type = "error", message = "The camera was deleted." },
                    context.RequestAborted);
            }
            catch (WebSocketException)
            {
            }

            await StreamWebSocket.CloseOutputAsync(
                socket,
                WebSocketCloseStatus.PolicyViolation,
                "Camera deleted",
                context.RequestAborted);
            return;
        }

        await streams.RunAsync(camera.Id, camera.Url, socket, context.RequestAborted);
    }
}
