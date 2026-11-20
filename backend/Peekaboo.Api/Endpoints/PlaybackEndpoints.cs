using Peekaboo.Api.Isapi;
using Peekaboo.Api.Playback;

namespace Peekaboo.Api.Endpoints;

public static class PlaybackEndpoints
{
    public static IEndpointRouteBuilder MapPlaybackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cameras/{id}/recordings", HandleRecordingsAsync)
            .RequireAuthorization();

        return app;
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
    }

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
