using System.Globalization;

namespace Peekaboo.Api.Cameras;

public enum CameraStream
{
    Main,
    Sub,
}

public sealed record CameraDto(
    string Id,
    string NvrId,
    string NvrName,
    string Name,
    string MainStreamId,
    string SubStreamId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CameraStreamTarget(
    string CameraId,
    string CameraName,
    string NvrId,
    string NvrTransport,
    string NvrHost,
    int SdkPort,
    string StreamId,
    CameraStream Stream,
    string Url);

public sealed record CameraPlaybackSource(
    string CameraId,
    string CameraName,
    string NvrId,
    string NvrTransport,
    string NvrHost,
    int SdkPort,
    string RtspBaseUrl,
    string MainStreamId,
    string NvrTimeZone);

public static class CameraValidation
{
    private const int MaximumNameLength = 100;
    private const int MaximumStreamIdLength = 6;

    public static string? NameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Camera name is required.";
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaximumNameLength)
        {
            return $"Camera name must be at most {MaximumNameLength} characters.";
        }

        return ContainsControlCharacters(trimmed)
            ? "Camera name must not contain control characters."
            : null;
    }

    public static string? StreamIdError(string? streamId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(streamId))
        {
            return $"{fieldName} is required.";
        }

        var trimmed = streamId.Trim();
        if (trimmed.Length > MaximumStreamIdLength ||
            !trimmed.All(char.IsAsciiDigit) ||
            !int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < 1)
        {
            return $"{fieldName} must be a positive number, for example 101.";
        }

        return null;
    }

    public static string NormalizeStreamId(string streamId) =>
        int.Parse(streamId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);

    private static bool ContainsControlCharacters(string value) =>
        value.Any(character => character < 0x20 || character == 0x7f);
}
