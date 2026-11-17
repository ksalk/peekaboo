using Microsoft.AspNetCore.Authorization;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Streaming;

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
        group.MapPost("/", CreateCamera);
        group.MapPut("/{id}", UpdateCamera);
        group.MapDelete("/{id}", DeleteCamera);

        return app;
    }

    private static IResult CreateCamera(CameraRequest request, CameraService cameras)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return validation;
        }

        var camera = cameras.Create(request.Name!.Trim(), request.Url!.Trim());
        return Results.Created($"/api/cameras/{camera.Id}", camera);
    }

    private static IResult UpdateCamera(string id, CameraRequest request, CameraService cameras)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return validation;
        }

        var camera = cameras.Update(id, request.Name!.Trim(), request.Url!.Trim());
        return camera is null ? Results.NotFound() : Results.Ok(camera);
    }

    private static async Task<IResult> DeleteCamera(
        string id,
        CameraService cameras,
        StreamManager streams)
    {
        if (!cameras.Delete(id))
        {
            return Results.NotFound();
        }

        await streams.StopCameraAsync(id, "The camera was deleted.");
        return Results.NoContent();
    }

    private static IResult? Validate(CameraRequest request)
    {
        var nameError = CameraValidation.NameError(request.Name);
        if (nameError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: nameError);
        }

        var urlError = CameraValidation.UrlError(request.Url);
        return urlError is null
            ? null
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: urlError);
    }
}
