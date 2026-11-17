namespace Peekaboo.Api.Streaming;

public static class StreamOrigin
{
    /// <summary>
    /// Browsers attach cookies to cross-site WebSocket handshakes, so browser
    /// connections should originate from the same host. Non-browser clients usually
    /// send no Origin header and are allowed (they still need a valid session).
    /// </summary>
    public static bool IsAllowed(string? origin, HostString host)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        if (!string.Equals(originUri.Host, host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (host.Port is { } requestPort)
        {
            return originUri.Port == requestPort;
        }

        // Host omits the port (common behind a reverse proxy). Trust either standard
        // web port; the host match above is what prevents cross-site WebSocket abuse.
        return originUri.Port is 80 or 443;
    }
}
