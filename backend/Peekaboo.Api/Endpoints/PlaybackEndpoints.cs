using Peekaboo.Api.Cameras;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Playback;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Endpoints;

public static class PlaybackEndpoints
{
    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cameras/{id}/recordings", HandleRecordingsAsync)
            .RequireAuthorization();

        app.MapGet("/api/cameras/{id}/recordings/stream", HandleRecordingStreamAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task HandleRecordingStreamAsync(
        string id,
        string? start,
        string? end,
        HttpContext context,
        CameraService cameras,
        PlaybackService playbacks,
        StreamManager streams,
        CancellationToken cancellationToken)
    {
        if (!StreamOrigin.IsAllowed(
                context.Request.Headers.Origin.ToString(),
                context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!PlaybackTimeRange.TryParse(start, end, out var from, out var to, out var rangeError))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: rangeError).ExecuteAsync(context);
            return;
        }

        if (to - from > PlaybackTimeRange.MaximumPlaybackDuration)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"A recording playback can span at most " +
                       $"{PlaybackTimeRange.MaximumPlaybackHours} hours.").ExecuteAsync(context);
            return;
        }

        if (cameras.GetPlaybackSource(id) is null)
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
            StreamKey.Playback(id, from, to),
            () => playbacks.GetStreamSource(id, from, to),
            socket,
            cancellationToken);
    }

    private static async Task<IResult> HandleRecordingsAsync(
        string id,
        string? start,
        string? end,
        PlaybackService playbacks,
        ILogger<PlaybackService> logger,
        CancellationToken cancellationToken)
    {
        if (!PlaybackTimeRange.TryParse(start, end, out var from, out var to, out var rangeError))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: rangeError);
        }

        try
        {
            var result = await playbacks.SearchAsync(id, from, to, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (IsapiException exception)
        {
            logger.LogWarning(
                exception,
                "Recording search for camera {CameraId} failed with {FailureKind}",
                id,
                exception.Kind);

            return MapIsapiFailure(exception);
        }
        catch (HikvisionException exception)
        {
            logger.LogWarning(
                exception,
                "SDK recording search for camera {CameraId} failed with {FailureKind}",
                id,
                exception.Kind);

            return MapHikvisionFailure(exception);
        }
    }

    private static IResult MapHikvisionFailure(HikvisionException exception) =>
        exception.Kind switch
        {
            HikvisionFailureKind.Authentication or HikvisionFailureKind.Permission =>
                Results.Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "The NVR rejected the SDK credentials or permissions. Check " +
                           "CAMERA_USERNAME, CAMERA_PASSWORD, and the NVR user's rights."),
            HikvisionFailureKind.Timeout => Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "The NVR did not answer the SDK recording search in time."),
            HikvisionFailureKind.Channel => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The camera's main stream ID is not valid for the SDK transport."),
            HikvisionFailureKind.Unavailable => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Could not reach the NVR SDK service."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR reported an error while searching recordings with the SDK."),
        };

    private static IResult MapIsapiFailure(IsapiException exception) =>
        exception.Kind switch
        {
            IsapiFailureKind.BadRequest => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: exception.Message),
            IsapiFailureKind.Timeout => Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "The NVR did not answer the recording search in time."),
            IsapiFailureKind.Unauthorized => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR rejected the ISAPI credentials. Check CAMERA_USERNAME " +
                       "and CAMERA_PASSWORD."),
            IsapiFailureKind.NotFound => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR does not expose the ISAPI recording search endpoint. " +
                       "Check the NVR address and firmware."),
            IsapiFailureKind.Unreachable => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Could not reach the NVR ISAPI service."),
            IsapiFailureKind.InvalidResponse => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR returned an invalid recording search response."),
            // A 401 here would make the browser think its Peekaboo session expired.
            _ => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR reported an error while searching recordings."),
        };
}
