using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Isapi;

public sealed class IsapiClientTests
{
    [Fact]
    public async Task Search_follows_more_pages_and_uses_the_documented_position()
    {
        var handler = new StubHandler((index, _) => index switch
        {
            0 => Xml(Result(
                "MORE",
                numOfMatches: 1,
                ("2026-11-20T10:00:00Z", "2026-11-20T10:05:00Z"))),
            1 => Xml(Result(
                "OK",
                numOfMatches: 1,
                ("2026-11-20T10:05:00Z", "2026-11-20T10:10:00Z"))),
            _ => throw new InvalidOperationException("Unexpected search page."),
        });

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchRecordingsAsync(Request(), CancellationToken.None);

        Assert.Equal(2, result.Segments.Count);
        Assert.False(result.Truncated);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("<searchResultPostion>0</searchResultPostion>", handler.Bodies[0]);
        Assert.Contains("<searchResultPostion>1</searchResultPostion>", handler.Bodies[1]);
    }

    [Fact]
    public async Task Search_deduplicates_overlapping_pages_and_sorts_by_start()
    {
        var handler = new StubHandler((index, _) => index switch
        {
            0 => Xml(Result(
                "MORE",
                numOfMatches: 1,
                ("2026-11-20T10:10:00Z", "2026-11-20T10:15:00Z"))),
            1 => Xml(Result(
                "OK",
                numOfMatches: 2,
                ("2026-11-20T10:10:00Z", "2026-11-20T10:15:00Z"),
                ("2026-11-20T10:00:00Z", "2026-11-20T10:05:00Z"))),
            _ => throw new InvalidOperationException("Unexpected search page."),
        });

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchRecordingsAsync(Request(), CancellationToken.None);

        Assert.Equal(2, result.Segments.Count);
        Assert.True(result.Segments[0].Start < result.Segments[1].Start);
    }

    [Fact]
    public async Task Search_returns_an_empty_result_for_no_matches()
    {
        var handler = new StubHandler((_, _) => Xml(Result("NO MATCHES", 0)));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchRecordingsAsync(Request(), CancellationToken.None);

        Assert.Empty(result.Segments);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Authentication_failures_are_not_returned_as_peekaboo_unauthorized()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchRecordingsAsync(Request(), CancellationToken.None));

        Assert.Equal(IsapiFailureKind.Unauthorized, exception.Kind);
        Assert.Contains("CAMERA_USERNAME", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_range_is_reported_as_bad_request()
    {
        var handler = new StubHandler((_, _) =>
            throw new InvalidOperationException("The HTTP client must not be called."));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        var request = Request();
        var invalid = request with { End = request.Start };

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchRecordingsAsync(invalid, CancellationToken.None));

        Assert.Equal(IsapiFailureKind.BadRequest, exception.Kind);
        Assert.Empty(handler.Bodies);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, IsapiFailureKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, IsapiFailureKind.DeviceError)]
    public async Task Http_failures_map_to_client_error_kinds(
        HttpStatusCode statusCode,
        IsapiFailureKind expectedKind)
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(statusCode));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchRecordingsAsync(Request(), CancellationToken.None));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task More_without_a_progress_count_is_reported_as_truncated()
    {
        var handler = new StubHandler((_, _) => Xml(Result(
            "MORE",
            numOfMatches: 0,
            ("2026-11-20T10:00:00Z", "2026-11-20T10:05:00Z"))));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchRecordingsAsync(Request(), CancellationToken.None);

        Assert.Single(result.Segments);
        Assert.True(result.Truncated);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task Exactly_one_thousand_results_without_more_is_not_truncated()
    {
        var baseTime = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var spans = Enumerable.Range(0, 1000)
            .Select(index => (
                IsapiTime.FormatSearch(baseTime.AddSeconds(index).UtcDateTime),
                IsapiTime.FormatSearch(baseTime.AddSeconds(index + 1).UtcDateTime)))
            .ToArray();

        var handler = new StubHandler((_, _) => Xml(Result("OK", 1000, spans)));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchRecordingsAsync(Request(), CancellationToken.None);

        Assert.Equal(1000, result.Segments.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Search_round_trips_device_local_times_as_utc()
    {
        var handler = new StubHandler((_, _) => Xml(Result(
            "OK",
            numOfMatches: 1,
            ("2026-11-20T11:00:00Z", "2026-11-20T11:05:00Z"))));
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchRecordingsAsync(Request(zone), CancellationToken.None);

        var segment = Assert.Single(result.Segments);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero),
            segment.Start);
        Assert.Contains("<startTime>2026-11-20T11:00:00Z</startTime>", handler.Bodies[0]);
        Assert.Contains("<endTime>2026-11-20T12:00:00Z</endTime>", handler.Bodies[0]);
    }

    [Fact]
    public async Task Malformed_responses_are_reported_as_invalid()
    {
        var handler = new StubHandler((_, _) => Xml("not xml"));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchRecordingsAsync(Request(), CancellationToken.None));

        Assert.Equal(IsapiFailureKind.InvalidResponse, exception.Kind);
    }

    [Fact]
    public async Task Device_errors_surface_their_message()
    {
        var handler = new StubHandler((_, _) => Xml(
            """
            <CMSearchResult>
              <responseStatus>false</responseStatus>
              <statusString>Invalid Operation</statusString>
            </CMSearchResult>
            """));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchRecordingsAsync(Request(), CancellationToken.None));

        Assert.Equal(IsapiFailureKind.DeviceError, exception.Kind);
        Assert.Contains("Invalid Operation", exception.Message, StringComparison.Ordinal);
    }

    private static IsapiClient CreateClient(HttpClient http)
    {
        var resolver = new IsapiEndpointResolver(
            http,
            new IsapiOptions(8666, InsecureTls: false),
            NullLogger<IsapiEndpointResolver>.Instance);

        return new IsapiClient(http, resolver, NullLogger<IsapiClient>.Instance);
    }

    private static IsapiSearchRequest Request(INvrTimeZone? timeZone = null) =>
        new(
            "nvr-1",
            "rtsp://nvr.local:554",
            "101",
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero),
            timeZone ?? TestNvrTimeZones.Utc);

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        };

    private static string Result(
        string status,
        int numOfMatches,
        params (string Start, string End)[] spans)
    {
        var matchList = new XElement("matchList");

        foreach (var (start, end) in spans)
        {
            matchList.Add(
                new XElement(
                    "searchMatchItem",
                    new XElement(
                        "timeSpan",
                        new XElement("startTime", start),
                        new XElement("endTime", end)),
                    new XElement(
                        "mediaSegmentDescriptor",
                        new XElement("contentType", "video"),
                        new XElement("codecType", "H.264-BP"))));
        }

        return new XDocument(
            new XElement(
                "CMSearchResult",
                new XElement("responseStatus", "true"),
                new XElement("responseStatusStrg", status),
                new XElement("numOfMatches", numOfMatches),
                matchList))
            .ToString(SaveOptions.DisableFormatting);
    }

    private sealed class StubHandler(Func<int, string, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        private int _calls;

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            return respond(_calls++, body);
        }
    }
}
