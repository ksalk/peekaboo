using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;

namespace Peekaboo.Api.Isapi;

public sealed class IsapiEndpointException(string message) : Exception(message);

/// <summary>
/// Finds the NVR's HTTP(S) ISAPI endpoint from its stored RTSP address. Port 554
/// is RTSP-only, so the host is probed on the standard HTTP and HTTPS ports.
/// Successful resolutions are cached per NVR and host.
/// </summary>
public sealed class IsapiEndpointResolver
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly IsapiOptions _options;
    private readonly ILogger<IsapiEndpointResolver> _logger;
    private readonly ConcurrentDictionary<string, CachedEndpoint> _cache = new(StringComparer.Ordinal);

    public IsapiEndpointResolver(
        IsapiHttpClient client,
        IsapiOptions options,
        ILogger<IsapiEndpointResolver> logger)
        : this(client.Client, options, logger)
    {
    }

    internal IsapiEndpointResolver(
        HttpClient http,
        IsapiOptions options,
        ILogger<IsapiEndpointResolver> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string> ResolveAsync(
        string nvrId,
        string rtspBaseUrl,
        CancellationToken cancellationToken)
    {
        var host = DeriveHost(rtspBaseUrl);

        if (_options.HttpPort is { } port)
        {
            return $"http://{host}:{port}";
        }

        var key = CacheKey(nvrId, host);
        if (_cache.TryGetValue(key, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.BaseUrl;
        }

        var resolved = await ProbeAsync(host, cancellationToken);
        _cache[key] = new CachedEndpoint(
            resolved,
            DateTimeOffset.UtcNow.Add(CacheLifetime));
        return resolved;
    }

    public void Invalidate(string nvrId, string rtspBaseUrl)
    {
        if (_options.HttpPort is not null)
        {
            return;
        }

        _cache.TryRemove(CacheKey(nvrId, DeriveHost(rtspBaseUrl)), out _);
    }

    public static string DeriveHost(string rtspBaseUrl)
    {
        if (!Uri.TryCreate(rtspBaseUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException(
                "The NVR RTSP base URL is invalid.",
                nameof(rtspBaseUrl));
        }

        return uri.Host;
    }

    private async Task<string> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        var candidates = new[] { $"http://{host}", $"https://{host}" };
        var failures = new List<string>(candidates.Length);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ProbeTimeout);

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{candidate}/ISAPI/System/deviceInfo");

                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if (!IsReachable(response.StatusCode))
                {
                    failures.Add($"{candidate} returned HTTP {(int)response.StatusCode}");
                    continue;
                }

                var resolved = ResolveRedirect(candidate, response);
                _logger.LogDebug("NVR ISAPI endpoint resolved to {Endpoint}", resolved);
                return resolved;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{candidate} timed out");
            }
            catch (HttpRequestException exception)
            {
                failures.Add($"{candidate}: {Describe(exception)}");
            }
        }

        var detail = string.Join("; ", failures);
        _logger.LogWarning(
            "ISAPI endpoint discovery failed for {Host}: {Detail}",
            host,
            detail);

        throw new IsapiEndpointException(
            $"Could not reach the NVR ISAPI service. Tried {string.Join(", ", candidates)}. " +
            $"{detail}. Check that the NVR web interface is enabled and reachable.");
    }

    private static bool IsReachable(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and < 400 ||
        statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden;

    private static string ResolveRedirect(string candidate, HttpResponseMessage response)
    {
        if ((int)response.StatusCode is >= 300 and < 400 &&
            response.Headers.Location is { } location)
        {
            var baseUri = new Uri($"{candidate}/", UriKind.Absolute);
            var target = location.IsAbsoluteUri ? location : new Uri(baseUri, location);

            if ((string.Equals(target.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(target.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return target.GetLeftPart(UriPartial.Authority);
            }

            throw new HttpRequestException(
                $"The NVR redirected to an unexpected address: {target.GetLeftPart(UriPartial.Authority)}.");
        }

        return candidate;
    }

    private static string Describe(HttpRequestException exception)
    {
        if (exception.InnerException is AuthenticationException)
        {
            return "the HTTPS certificate is not trusted; set " +
                   "PEEKABOO_ISAPI_INSECURE_TLS=true on a trusted network";
        }

        return exception.Message;
    }

    private static string CacheKey(string nvrId, string host) => $"{nvrId}|{host}";

    private sealed record CachedEndpoint(string BaseUrl, DateTimeOffset ExpiresAt);
}
