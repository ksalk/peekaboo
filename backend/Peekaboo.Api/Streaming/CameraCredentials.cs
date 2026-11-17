namespace Peekaboo.Api.Streaming;

/// <summary>
/// Resolves the single global camera credential pair from the environment and
/// injects it into stored camera URLs only at stream start. Credentials are never
/// persisted and never sent to the browser.
/// </summary>
public sealed class CameraCredentials
{
    private readonly string? _username;
    private readonly string? _password;

    private CameraCredentials(string? username, string? password)
    {
        _username = username;
        _password = password;
    }

    public bool HasCredentials => _username is not null;

    public static CameraCredentials FromConfiguration(IConfiguration configuration)
    {
        var username = NullIfEmpty(configuration["CAMERA_USERNAME"]?.Trim());
        var password = NullIfEmpty(configuration["CAMERA_PASSWORD"]);

        if ((username is null) != (password is null))
        {
            throw new InvalidOperationException(
                "CAMERA_USERNAME and CAMERA_PASSWORD must be set together, or both left empty.");
        }

        return new CameraCredentials(username, password);
    }

    public string Resolve(string cameraUrl)
    {
        if (_username is null)
        {
            return cameraUrl;
        }

        var schemeEnd = cameraUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return cameraUrl;
        }

        var userInfo = string.Concat(
            Uri.EscapeDataString(_username),
            ":",
            Uri.EscapeDataString(_password!));

        return string.Concat(
            cameraUrl.AsSpan(0, schemeEnd + 3),
            userInfo,
            "@",
            cameraUrl.AsSpan(schemeEnd + 3));
    }

    public string Redact(string text, string cameraUrl)
    {
        if (_username is null)
        {
            return text;
        }

        var redacted = text.Replace(Resolve(cameraUrl), cameraUrl, StringComparison.Ordinal);
        return redacted.Replace(_password!, "***", StringComparison.Ordinal);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
