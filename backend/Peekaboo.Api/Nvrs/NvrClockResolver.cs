using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Peekaboo.Api.Isapi;

namespace Peekaboo.Api.Nvrs;

public sealed record NvrClock(
    INvrTimeZone TimeZone,
    string Source,
    string? DetectedTimeZone,
    DateTimeOffset RetrievedAt,
    string? Error);

public interface INvrClockResolver
{
    Task<NvrClock> ResolveAsync(
        string nvrId,
        string baseUrl,
        string timeZone,
        CancellationToken cancellationToken);

    void Invalidate(string nvrId);
}

/// <summary>
/// Resolves the NVR clock semantics. An explicit IANA time zone from the NVR
/// settings always wins. Otherwise Peekaboo reads <c>/ISAPI/System/time</c> and
/// parses the device's POSIX time-zone string, including DST rules.
/// </summary>
public sealed class NvrClockResolver : INvrClockResolver
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(1);
    private static readonly Regex ExplicitOffsetPattern = new(
        "(?:Z|[+-]\\d{2}:?\\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly IsapiEndpointResolver _endpoints;
    private readonly ILogger<NvrClockResolver> _logger;
    private readonly ConcurrentDictionary<string, CachedClock> _cache = new(StringComparer.Ordinal);

    public NvrClockResolver(
        IsapiHttpClient http,
        IsapiEndpointResolver endpoints,
        ILogger<NvrClockResolver> logger)
        : this(http.Client, endpoints, logger)
    {
    }

    internal NvrClockResolver(
        HttpClient http,
        IsapiEndpointResolver endpoints,
        ILogger<NvrClockResolver> logger)
    {
        _http = http;
        _endpoints = endpoints;
        _logger = logger;
    }

    public Task<NvrClock> ResolveAsync(
        NvrDto nvr,
        CancellationToken cancellationToken) =>
        ResolveAsync(nvr.Id, nvr.BaseUrl, nvr.TimeZone, cancellationToken);

    public async Task<NvrClock> ResolveAsync(
        string nvrId,
        string baseUrl,
        string timeZone,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(nvrId, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow &&
            string.Equals(cached.BaseUrl, baseUrl, StringComparison.Ordinal) &&
            string.Equals(cached.TimeZone, timeZone, StringComparison.Ordinal))
        {
            return cached.Clock;
        }

        var clock = await ResolveCoreAsync(
            nvrId,
            baseUrl,
            timeZone,
            cancellationToken);
        var lifetime = clock.Error is null ? CacheLifetime : FailureLifetime;

        _cache[nvrId] = new CachedClock(
            baseUrl,
            timeZone,
            clock,
            DateTimeOffset.UtcNow.Add(lifetime));
        return clock;
    }

    public void Invalidate(string nvrId) => _cache.TryRemove(nvrId, out _);

    private async Task<NvrClock> ResolveCoreAsync(
        string nvrId,
        string baseUrl,
        string timeZone,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            if (TryFindTimeZone(timeZone, out var configured))
            {
                return new NvrClock(
                    new TimeZoneInfoNvrTimeZone(configured),
                    "override",
                    timeZone,
                    DateTimeOffset.UtcNow,
                    Error: null);
            }

            var error = $"The configured NVR time zone '{timeZone}' is not valid.";
            _logger.LogWarning(
                "NVR {NvrId} has an invalid time zone override: {TimeZone}",
                nvrId,
                timeZone);
            return Fallback(error);
        }

        try
        {
            var endpoint = await _endpoints.ResolveAsync(
                nvrId,
                baseUrl,
                cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{endpoint}/ISAPI/System/time");
            request.Headers.Accept.ParseAdd("application/xml");

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Fallback(
                    $"The NVR returned HTTP {(int)response.StatusCode} for its clock.");
            }

            var body = await ReadBodyAsync(response, cancellationToken);
            return ParseClock(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fallback("The NVR did not answer the clock request in time.");
        }
        catch (IsapiEndpointException exception)
        {
            return Fallback(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return Fallback($"Could not reach the NVR clock: {exception.Message}");
        }
        catch (Exception exception) when (
            exception is XmlException or InvalidOperationException)
        {
            return Fallback($"The NVR returned an invalid clock response: {exception.Message}");
        }
    }

    private NvrClock ParseClock(string body)
    {
        var document = XDocument.Parse(body.TrimStart('\uFEFF'));
        var localTime = FindValue(document, "localTime");
        var timeZone = FindValue(document, "timeZone");

        if (!string.IsNullOrWhiteSpace(timeZone) &&
            PosixNvrTimeZone.TryParse(timeZone, out var parsedZone) &&
            parsedZone is not null)
        {
            _logger.LogInformation(
                "Detected NVR time zone {TimeZone} from its clock endpoint",
                parsedZone.Id);
            return new NvrClock(
                parsedZone,
                "detected",
                parsedZone.Id,
                DateTimeOffset.UtcNow,
                Error: null);
        }

        if (!string.IsNullOrWhiteSpace(localTime) &&
            ExplicitOffsetPattern.IsMatch(localTime) &&
            DateTimeOffset.TryParse(
                localTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedLocalTime))
        {
            var offset = parsedLocalTime.Offset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var zone = new FixedNvrTimeZone(
                $"UTC{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}",
                offset);
            _logger.LogWarning(
                "NVR returned time zone '{TimeZone}' which could not be parsed; " +
                "using its current offset {Offset} without DST rules",
                timeZone,
                parsedLocalTime.Offset);
            return new NvrClock(
                zone,
                "detected-offset",
                timeZone,
                DateTimeOffset.UtcNow,
                Error: null);
        }

        return Fallback("The NVR did not return a usable time zone.");
    }

    private static string? FindValue(XDocument document, string name) =>
        document.Root
            ?.DescendantsAndSelf()
            .FirstOrDefault(element => string.Equals(
                element.Name.LocalName,
                name,
                StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumResponseBytes)
            {
                throw new InvalidOperationException("The NVR clock response was too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool TryFindTimeZone(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }

    private static NvrClock Fallback(string error) =>
        new(
            new FixedNvrTimeZone("UTC", TimeSpan.Zero),
            "fallback",
            DetectedTimeZone: null,
            DateTimeOffset.UtcNow,
            error);

    private sealed record CachedClock(
        string BaseUrl,
        string TimeZone,
        NvrClock Clock,
        DateTimeOffset ExpiresAt);
}
