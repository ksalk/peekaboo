namespace Peekaboo.Api.Nvrs;

public sealed record NvrDto(
    string Id,
    string Name,
    string BaseUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class NvrValidation
{
    private const int MaximumNameLength = 100;
    private const int MaximumBaseUrlLength = 2048;

    public static string? NameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "NVR name is required.";
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaximumNameLength)
        {
            return $"NVR name must be at most {MaximumNameLength} characters.";
        }

        return ContainsControlCharacters(trimmed)
            ? "NVR name must not contain control characters."
            : null;
    }

    public static string? BaseUrlError(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "NVR base URL is required.";
        }

        var trimmed = baseUrl.Trim();
        if (trimmed.Length > MaximumBaseUrlLength)
        {
            return $"NVR base URL must be at most {MaximumBaseUrlLength} characters.";
        }

        if (ContainsControlCharacters(trimmed))
        {
            return "NVR base URL must not contain control characters.";
        }

        if (ContainsNonAsciiCharacters(trimmed))
        {
            return "NVR base URL must contain only ASCII characters.";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return "NVR base URL must be an absolute RTSP URL.";
        }

        if (!string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase))
        {
            return "NVR base URL must use the rtsp:// or rtsps:// scheme.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "NVR base URL must not contain credentials; set CAMERA_USERNAME and " +
                   "CAMERA_PASSWORD in the environment instead.";
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return "NVR base URL must include a host.";
        }

        if (uri.Port == 0)
        {
            return "NVR base URL must use a valid RTSP port.";
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "NVR base URL must not contain a query or fragment.";
        }

        return uri.AbsolutePath is "" or "/"
            ? null
            : "NVR base URL must not contain a path; use the host and RTSP port only.";
    }

    /// <summary>
    /// Returns the canonical stored form: lowercase scheme and authority only. Using
    /// the parsed URI prevents dot segments or other path remnants from being stored.
    /// </summary>
    public static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.TrimEnd('/');
        }

        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority.ToLowerInvariant()}";
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(character => character < 0x20 || character == 0x7f);

    private static bool ContainsNonAsciiCharacters(string value) =>
        value.Any(character => character > 0x7e);
}
