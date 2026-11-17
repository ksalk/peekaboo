namespace Peekaboo.Api.Cameras;

public sealed record CameraDto(
    string Id,
    string Name,
    string Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class CameraValidation
{
    private const int MaximumNameLength = 100;
    private const int MaximumUrlLength = 2048;

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

    public static string? UrlError(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Camera URL is required.";
        }

        var trimmed = url.Trim();
        if (trimmed.Length > MaximumUrlLength)
        {
            return $"Camera URL must be at most {MaximumUrlLength} characters.";
        }

        if (ContainsControlCharacters(trimmed))
        {
            return "Camera URL must not contain control characters.";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return "Camera URL must be an absolute RTSP URL.";
        }

        if (!string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase))
        {
            return "Camera URL must use the rtsp:// or rtsps:// scheme.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Camera URL must not contain credentials; set CAMERA_USERNAME and " +
                   "CAMERA_PASSWORD in the environment instead.";
        }

        return string.IsNullOrEmpty(uri.Host)
            ? "Camera URL must include a host."
            : null;
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(character => character < 0x20 || character == 0x7f);
}
