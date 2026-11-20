using Peekaboo.Api.Cameras;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Endpoints;

public static class NvrEndpoints
{
    public static IEndpointRouteBuilder MapNvrEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/nvrs")
            .RequireAuthorization();

        group.MapGet("/", (NvrService nvrs) => Results.Ok(nvrs.List()));
        group.MapGet("/{id}", (string id, NvrService nvrs) =>
        {
            var nvr = nvrs.Get(id);
            return nvr is null ? Results.NotFound() : Results.Ok(nvr);
        });
        group.MapPost("/", CreateNvr);
        group.MapPut("/{id}", UpdateNvr);
        group.MapDelete("/{id}", DeleteNvr);

        group.MapPost("/{id}/cameras", CreateCamera);
        group.MapPut("/{id}/cameras/{cameraId}", UpdateCamera);
        group.MapDelete("/{id}/cameras/{cameraId}", DeleteCamera);

        return app;
    }

    private static IResult CreateNvr(NvrRequest request, NvrService nvrs)
    {
        var validation = ValidateNvr(request);
        if (validation is not null)
        {
            return validation;
        }

        var nvr = nvrs.Create(
            request.Name!.Trim(),
            NvrValidation.NormalizeBaseUrl(request.BaseUrl!));

        return Results.Created($"/api/nvrs/{nvr.Id}", nvr);
    }

    private static async Task<IResult> UpdateNvr(
        string id,
        NvrRequest request,
        NvrService nvrs,
        CameraService cameras,
        StreamManager streams,
        IsapiEndpointResolver isapiEndpoints)
    {
        var validation = ValidateNvr(request);
        if (validation is not null)
        {
            return validation;
        }

        var existing = nvrs.Get(id);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var updated = nvrs.Update(
            id,
            request.Name!.Trim(),
            NvrValidation.NormalizeBaseUrl(request.BaseUrl!));

        if (updated is null)
        {
            return Results.NotFound();
        }

        if (!string.Equals(existing.BaseUrl, updated.BaseUrl, StringComparison.Ordinal))
        {
            isapiEndpoints.Invalidate(id, existing.BaseUrl);
            isapiEndpoints.Invalidate(id, updated.BaseUrl);

            var cameraIds = cameras.ListCameraIdsForNvr(id);
            await streams.StopCamerasAsync(cameraIds, "The NVR configuration was updated.");
        }

        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteNvr(
        string id,
        NvrService nvrs,
        StreamManager streams,
        IsapiEndpointResolver isapiEndpoints)
    {
        var existing = nvrs.Get(id);
        var cameraIds = nvrs.Delete(id);
        if (cameraIds is null)
        {
            return Results.NotFound();
        }

        if (existing is not null)
        {
            isapiEndpoints.Invalidate(id, existing.BaseUrl);
        }

        await streams.StopCamerasAsync(cameraIds, "The NVR was deleted.");
        return Results.NoContent();
    }

    private static IResult CreateCamera(
        string id,
        CameraRequest request,
        NvrService nvrs,
        CameraService cameras)
    {
        if (nvrs.Get(id) is null)
        {
            return Results.NotFound();
        }

        var validation = ValidateCamera(request);
        if (validation is not null)
        {
            return validation;
        }

        var camera = cameras.Create(
            id,
            request.Name!.Trim(),
            CameraValidation.NormalizeStreamId(request.MainStreamId!),
            CameraValidation.NormalizeStreamId(request.SubStreamId!));

        return camera is null
            ? Results.NotFound()
            : Results.Created($"/api/nvrs/{id}/cameras/{camera.Id}", camera);
    }

    private static async Task<IResult> UpdateCamera(
        string id,
        string cameraId,
        CameraRequest request,
        NvrService nvrs,
        CameraService cameras,
        StreamManager streams)
    {
        if (nvrs.Get(id) is null)
        {
            return Results.NotFound();
        }

        var validation = ValidateCamera(request);
        if (validation is not null)
        {
            return validation;
        }

        var existing = cameras.Get(cameraId);
        if (existing is null || !string.Equals(existing.NvrId, id, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var updated = cameras.Update(
            id,
            cameraId,
            request.Name!.Trim(),
            CameraValidation.NormalizeStreamId(request.MainStreamId!),
            CameraValidation.NormalizeStreamId(request.SubStreamId!));

        if (updated is null)
        {
            return Results.NotFound();
        }

        if (!string.Equals(existing.MainStreamId, updated.MainStreamId, StringComparison.Ordinal) ||
            !string.Equals(existing.SubStreamId, updated.SubStreamId, StringComparison.Ordinal))
        {
            await streams.StopCameraAsync(cameraId, "The camera stream configuration was updated.");
        }

        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteCamera(
        string id,
        string cameraId,
        CameraService cameras,
        StreamManager streams)
    {
        if (!cameras.Delete(id, cameraId))
        {
            return Results.NotFound();
        }

        await streams.StopCameraAsync(cameraId, "The camera was deleted.");
        return Results.NoContent();
    }

    private static IResult? ValidateNvr(NvrRequest request)
    {
        var nameError = NvrValidation.NameError(request.Name);
        if (nameError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: nameError);
        }

        var baseUrlError = NvrValidation.BaseUrlError(request.BaseUrl);
        return baseUrlError is null
            ? null
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: baseUrlError);
    }

    private static IResult? ValidateCamera(CameraRequest request)
    {
        var nameError = CameraValidation.NameError(request.Name);
        if (nameError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: nameError);
        }

        var mainError = CameraValidation.StreamIdError(request.MainStreamId, "Main stream ID");
        if (mainError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: mainError);
        }

        var subError = CameraValidation.StreamIdError(request.SubStreamId, "Sub stream ID");
        return subError is null
            ? null
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: subError);
    }
}
