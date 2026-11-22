using System.Net;
using System.Text;

using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

public sealed class IsapiAlarmLogClient : IIsapiAlarmLogClient
{
    private const int PageSize = 100;
    private const int MaximumResults = 10_000;
    private const int MaximumPages = 200;
    private const long MaximumResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly IsapiEndpointResolver _endpoints;
    private readonly ILogger<IsapiAlarmLogClient> _logger;
    private readonly TimeSpan _totalTimeout;

    public IsapiAlarmLogClient(
        IsapiHttpClient http,
        IsapiEndpointResolver endpoints,
        ILogger<IsapiAlarmLogClient> logger)
        : this(http.Client, endpoints, logger)
    {
    }

    internal IsapiAlarmLogClient(
        HttpClient http,
        IsapiEndpointResolver endpoints,
        ILogger<IsapiAlarmLogClient> logger,
        TimeSpan? totalTimeout = null)
    {
        _http = http;
        _endpoints = endpoints;
        _logger = logger;
        _totalTimeout = totalTimeout ?? TotalTimeout;
    }

    public async Task<IsapiAlarmLogResult> SearchAsync(
        IsapiAlarmLogRequest request,
        CancellationToken cancellationToken)
    {
        if (request.End <= request.Start)
        {
            throw new IsapiException(
                "The alarm log search end must be after its start.",
                IsapiFailureKind.BadRequest);
        }

        var endpoint = await ResolveEndpointAsync(request, cancellationToken);
        var searchId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var entries = new List<IsapiAlarmLogEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pageSignatures = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        var truncated = false;
        var completed = false;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_totalTimeout);

        try
        {
            for (var pageIndex = 0; pageIndex < MaximumPages; pageIndex++)
            {
                deadline.Token.ThrowIfCancellationRequested();

                var xml = IsapiAlarmLogXml.Build(
                    searchId,
                    request.Start,
                    request.End,
                    request.TimeZone,
                    PageSize,
                    position);

                var page = await SendSearchAsync(
                    endpoint,
                    xml,
                    request,
                    deadline.Token);

                if (page.DeviceError is not null)
                {
                    throw new IsapiException(page.DeviceError, IsapiFailureKind.DeviceError);
                }

                foreach (var entry in page.Entries)
                {
                    var key = string.IsNullOrEmpty(entry.Fingerprint)
                        ? $"{entry.Time.UtcTicks}|{entry.ChannelId}|{entry.MetaId}|{entry.TargetLabel}"
                        : entry.Fingerprint;

                    if (seen.Add(key))
                    {
                        entries.Add(entry);
                    }
                }

                if (page.Entries.Count > 0)
                {
                    var signature = string.Join(
                        '\u001f',
                        page.Entries.Select(entry => entry.Fingerprint)) +
                        $"|{page.RowCount}|{page.NumOfMatches}";

                    if (!pageSignatures.Add(signature))
                    {
                        truncated = true;
                        break;
                    }
                }

                if (!string.Equals(page.Status, "MORE", StringComparison.OrdinalIgnoreCase))
                {
                    if (position > 0 &&
                        string.Equals(page.Status, "NO MATCHES", StringComparison.OrdinalIgnoreCase))
                    {
                        truncated = true;
                    }

                    completed = true;
                    break;
                }

                if (entries.Count >= MaximumResults)
                {
                    truncated = true;
                    break;
                }

                var advance = page.RowCount > 0 ? page.RowCount : page.NumOfMatches;
                if (advance <= 0)
                {
                    truncated = true;
                    break;
                }

                position += advance;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IsapiException(
                "The NVR did not finish the alarm log search in time.",
                IsapiFailureKind.Timeout);
        }

        if (!completed)
        {
            truncated = true;
        }

        if (entries.Count > MaximumResults)
        {
            entries = entries.Take(MaximumResults).ToList();
            truncated = true;
        }

        entries = entries
            .OrderBy(entry => entry.Time)
            .ThenBy(entry => entry.Kind == IsapiAlarmLogKind.MotionStart ? 0 : 1)
            .ThenBy(entry => entry.Fingerprint, StringComparer.Ordinal)
            .ToList();

        _logger.LogDebug(
            "Alarm log search for NVR {NvrId} returned {Count} motion entries " +
            "(truncated: {Truncated})",
            request.NvrId,
            entries.Count,
            truncated);

        return new IsapiAlarmLogResult(entries, truncated);
    }

    private async Task<string> ResolveEndpointAsync(
        IsapiAlarmLogRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _endpoints.ResolveAsync(
                request.NvrId,
                request.RtspBaseUrl,
                cancellationToken);
        }
        catch (IsapiEndpointException exception)
        {
            throw new IsapiException(exception.Message, IsapiFailureKind.Unreachable);
        }
    }

    private async Task<IsapiAlarmLogPage> SendSearchAsync(
        string endpoint,
        string xml,
        IsapiAlarmLogRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{endpoint}/ISAPI/ContentMgmt/logSearch")
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        };
        message.Headers.Accept.ParseAdd("application/xml");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SearchTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IsapiException(
                "The NVR did not answer the alarm log search in time.",
                IsapiFailureKind.Timeout);
        }
        catch (HttpRequestException exception)
        {
            _endpoints.Invalidate(request.NvrId, request.RtspBaseUrl);
            throw new IsapiException(
                $"Could not reach the NVR ISAPI service: {exception.Message}",
                IsapiFailureKind.Unreachable);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new IsapiException(
                    "The NVR rejected the ISAPI request. Check CAMERA_USERNAME and " +
                    "CAMERA_PASSWORD and the NVR user's log permissions.",
                    IsapiFailureKind.Unauthorized);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new IsapiException(
                    "The NVR does not expose the ISAPI log search endpoint. " +
                    "Check the NVR address and firmware.",
                    IsapiFailureKind.NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    _endpoints.Invalidate(request.NvrId, request.RtspBaseUrl);
                }

                throw new IsapiException(
                    $"The NVR returned HTTP {(int)response.StatusCode} for the alarm log search.",
                    IsapiFailureKind.DeviceError);
            }

            try
            {
                var body = await ReadBodyAsync(response, timeout.Token);
                return IsapiAlarmLogXml.Parse(body, request.TimeZone);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IsapiException(
                    "The NVR did not finish the alarm log search in time.",
                    IsapiFailureKind.Timeout);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                _endpoints.Invalidate(request.NvrId, request.RtspBaseUrl);
                throw new IsapiException(
                    $"The NVR connection failed while reading the alarm log search: " +
                    $"{exception.Message}",
                    IsapiFailureKind.Unreachable);
            }
        }
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new IsapiException(
                "The NVR response was too large.",
                IsapiFailureKind.InvalidResponse);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long total = 0;

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
                throw new IsapiException(
                    "The NVR response was too large.",
                    IsapiFailureKind.InvalidResponse);
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
