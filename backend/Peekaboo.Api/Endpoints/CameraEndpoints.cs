using Peekaboo.Api.Cameras;

namespace Peekaboo.Api.Endpoints;

public static class CameraEndpoints
{
    public static IEndpointRouteBuilder MapCameraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cameras")
            .RequireAuthorization();

        group.MapGet("/", (CameraService cameras) => Results.Ok(cameras.List()));
        group.MapGet("/{id}", (string id, CameraService cameras) =>
        {
            var camera = cameras.Get(id);
            return camera is null ? Results.NotFound() : Results.Ok(camera);
        });

        return app;
    }
}
