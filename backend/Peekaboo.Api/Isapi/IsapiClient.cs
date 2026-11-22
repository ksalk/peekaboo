using System.Net;
using System.Text;

using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

public sealed class IsapiClient : IIsapiClient
{
    private const int PageSize = 100;
    private const int MaximumResults = 1000;
    private const int MaximumPages = 20;
    private const long MaximumResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly IsapiEndpointResolver _endpoints;
    private readonly ILogger<IsapiClient> _logger;

    public IsapiClient(
        IsapiHttpClient http,
        IsapiEndpointResolver endpoints,
        ILogger<IsapiClient> logger)
        : this(http.Client, endpoints, logger)
    {
    }

    internal IsapiClient(
        HttpClient http,
        IsapiEndpointResolver endpoints,
        ILogger<IsapiClient> logger)
    {
        _http = http;
        _endpoints = endpoints;
        _logger = logger;
    }

    public async Task<IsapiSearchResult> SearchRecordingsAsync(
        IsapiSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.End <= request.Start)
        {
            throw new IsapiException(
                "The recording search end must be after its start.",
                IsapiFailureKind.BadRequest);
        }

        var endpoint = await ResolveEndpointAsync(request, cancellationToken);
        var searchId = Guid.NewGuid().ToString("D").ToUpperInvariant();
        var segments = new List<IsapiRecordingSegment>();
        var seen = new HashSet<(long Start, long End)>();
        var position = 0;
        var truncated = false;

        for (var pageIndex = 0; pageIndex < MaximumPages; pageIndex++)
        {
            var xml = IsapiSearchXml.Build(
                searchId,
                request.TrackId,
                request.Start,
                request.End,
                request.TimeZone,
                PageSize,
                position);

            var page = await SendSearchAsync(
                endpoint,
                xml,
                request,
                cancellationToken);

            if (page.DeviceError is not null)
            {
                throw new IsapiException(page.DeviceError, IsapiFailureKind.DeviceError);
            }

            foreach (var segment in page.Segments)
            {
                var key = (segment.Start.UtcTicks, segment.End.UtcTicks);
                if (seen.Add(key))
                {
                    segments.Add(segment);
                }
            }

            if (!string.Equals(page.Status, "MORE", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (segments.Count >= MaximumResults)
            {
                truncated = true;
                break;
            }

            if (page.NumOfMatches <= 0)
            {
                truncated = true;
                break;
            }

            position += page.NumOfMatches;

            if (pageIndex == MaximumPages - 1)
            {
                truncated = true;
            }
        }

        if (segments.Count > MaximumResults)
        {
            segments = segments.Take(MaximumResults).ToList();
            truncated = true;
        }

        segments.Sort((left, right) => left.Start.CompareTo(right.Start));

        _logger.LogDebug(
            "Recording search for track {TrackId} returned {Count} segments (truncated: {Truncated})",
            request.TrackId,
            segments.Count,
            truncated);

        return new IsapiSearchResult(segments, truncated);
    }

    private async Task<string> ResolveEndpointAsync(
        IsapiSearchRequest request,
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

    private async Task<IsapiSearchPage> SendSearchAsync(
        string endpoint,
        string xml,
        IsapiSearchRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{endpoint}/ISAPI/ContentMgmt/search")
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
                "The NVR did not answer the recording search in time.",
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
                    "CAMERA_PASSWORD and the NVR user's permissions.",
                    IsapiFailureKind.Unauthorized);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new IsapiException(
                    "The NVR does not expose the ISAPI recording search endpoint. " +
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
                    $"The NVR returned HTTP {(int)response.StatusCode} for the recording search.",
                    IsapiFailureKind.DeviceError);
            }

            try
            {
                var body = await ReadBodyAsync(response, timeout.Token);
                return IsapiSearchXml.Parse(body, request.TimeZone);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IsapiException(
                    "The NVR did not finish the recording search in time.",
                    IsapiFailureKind.Timeout);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                _endpoints.Invalidate(request.NvrId, request.RtspBaseUrl);
                throw new IsapiException(
                    $"The NVR connection failed while reading the recording search: " +
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
