using Peekaboo.Api.Cameras;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Alarms;

public static class AlarmEndpoints
{
    public static IEndpointRouteBuilder MapAlarmEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/alarms", HandleAlarmsAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleAlarmsAsync(
        string? cameraId,
        string? nvrId,
        string? start,
        string? end,
        string? target,
        AlarmRangeService alarms,
        CameraService cameras,
        NvrService nvrs,
        ILogger<AlarmRangeService> logger,
        CancellationToken cancellationToken)
    {
        if (!AlarmTimeRange.TryParse(start, end, out var from, out var to, out var rangeError))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: rangeError);
        }

        cameraId = string.IsNullOrWhiteSpace(cameraId) ? null : cameraId.Trim();
        nvrId = string.IsNullOrWhiteSpace(nvrId) ? null : nvrId.Trim();

        if (!TryParseTarget(target, out var targetFilter, out var targetError))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: targetError);
        }

        if (cameraId is not null)
        {
            var camera = cameras.Get(cameraId);
            if (camera is null)
            {
                return Results.NotFound();
            }

            if (nvrId is not null &&
                !string.Equals(camera.NvrId, nvrId, StringComparison.Ordinal))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The camera does not belong to the requested NVR.");
            }
        }
        else if (nvrId is not null && nvrs.Get(nvrId) is null)
        {
            return Results.NotFound();
        }

        try
        {
            var result = await alarms.SearchAsync(
                cameraId,
                nvrId,
                from,
                to,
                targetFilter,
                cancellationToken);

            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (IsapiException exception)
        {
            logger.LogWarning(
                exception,
                "Alarm log search failed with {FailureKind}",
                exception.Kind);

            return MapIsapiFailure(exception);
        }
    }

    private static bool TryParseTarget(
        string? value,
        out AlarmTarget? target,
        out string? error)
    {
        target = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "any", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "unknown":
                target = AlarmTarget.Unknown;
                return true;
            case "human":
                target = AlarmTarget.Human;
                return true;
            case "vehicle":
                target = AlarmTarget.Vehicle;
                return true;
            default:
                error = "Target must be any, unknown, human, or vehicle.";
                return false;
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
                title: "The NVR did not answer the alarm log search in time."),
            IsapiFailureKind.Unauthorized => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR rejected the ISAPI credentials or the NVR user's " +
                       "log permissions. Check CAMERA_USERNAME and CAMERA_PASSWORD."),
            IsapiFailureKind.NotFound => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR does not expose the ISAPI log search endpoint. " +
                       "Check the NVR address and firmware."),
            IsapiFailureKind.Unreachable => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Could not reach the NVR ISAPI service."),
            IsapiFailureKind.InvalidResponse => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR returned an invalid alarm log response."),
            // A 401 here would make the browser think its Peekaboo session expired.
            _ => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The NVR reported an error while searching alarm logs."),
        };
}
