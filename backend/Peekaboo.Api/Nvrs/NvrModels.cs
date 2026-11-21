namespace Peekaboo.Api.Nvrs;

public sealed record NvrDto(
    string Id,
    string Name,
    string BaseUrl,
    string Transport,
    int SdkPort,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class NvrTransports
{
    public const string Rtsp = "rtsp";
    public const string HikvisionSdk = "sdk";
    public const int DefaultSdkPort = 8000;

    public static bool IsValid(string? transport) =>
        transport is Rtsp or HikvisionSdk;
}

public static class NvrValidation
{
    private const int MaximumNameLength = 100;
    private const int MaximumBaseUrlLength = 2048;
    private const int DefaultRtspPort = 554;

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
            return "NVR address is required.";
        }

        var trimmed = baseUrl.Trim();
        if (trimmed.Length > MaximumBaseUrlLength)
        {
            return $"NVR address must be at most {MaximumBaseUrlLength} characters.";
        }

        if (ContainsControlCharacters(trimmed))
        {
            return "NVR address must not contain control characters.";
        }

        if (ContainsNonAsciiCharacters(trimmed))
        {
            return "NVR address must contain only ASCII characters.";
        }

        var candidate = ToAbsoluteCandidate(trimmed);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return "NVR address must be a hostname, IP address, or absolute rtsp:// URL. " +
                   "IPv6 addresses need brackets, for example [::1].";
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return "NVR address must be a hostname, IP address, or rtsp:// URL, not an HTTP URL.";
        }

        if (!string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "rtsps", StringComparison.OrdinalIgnoreCase))
        {
            return "NVR address must use the rtsp:// or rtsps:// scheme.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "NVR address must not contain credentials; set CAMERA_USERNAME and " +
                   "CAMERA_PASSWORD in the environment instead.";
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return "NVR address must include a host.";
        }

        if (uri.Port == 0)
        {
            return "NVR address must use a valid RTSP port.";
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "NVR address must not contain a query or fragment.";
        }

        return uri.AbsolutePath is "" or "/"
            ? null
            : "NVR address must not contain a path; use the host and RTSP port only.";
    }

    public static string? TransportError(string? transport) =>
        NvrTransports.IsValid(transport)
            ? null
            : $"NVR transport must be '{NvrTransports.Rtsp}' or '{NvrTransports.HikvisionSdk}'.";

    public static string? SdkPortError(int sdkPort) =>
        sdkPort is >= 1 and <= 65535
            ? null
            : "The SDK port must be between 1 and 65535.";

    /// <summary>
    /// Returns the canonical stored form: lowercase scheme and authority only. A bare
    /// host or IP address is expanded to rtsp://&lt;host&gt;:554. Using the parsed URI
    /// prevents dot segments or other path remnants from being stored.
    /// </summary>
    public static string NormalizeBaseUrl(string baseUrl)
    {
        if (BaseUrlError(baseUrl) is not null)
        {
            throw new ArgumentException("The NVR address is invalid.", nameof(baseUrl));
        }

        var trimmed = baseUrl.Trim();
        var hasScheme = HasScheme(trimmed);
        var candidate = ToAbsoluteCandidate(trimmed);

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return trimmed.TrimEnd('/');
        }

        var authority = uri.Authority.ToLowerInvariant();
        if (!hasScheme && uri.Port <= 0)
        {
            authority = $"{uri.Host.ToLowerInvariant()}:{DefaultRtspPort}";
        }

        return $"{uri.Scheme.ToLowerInvariant()}://{authority}";
    }

    private static string ToAbsoluteCandidate(string value) =>
        HasScheme(value) ? value : $"rtsp://{value}";

    private static bool HasScheme(string value) =>
        value.Contains("://", StringComparison.Ordinal);

    private static bool ContainsControlCharacters(string value) =>
        value.Any(character => character < 0x20 || character == 0x7f);

    private static bool ContainsNonAsciiCharacters(string value) =>
        value.Any(character => character > 0x7e);
}
