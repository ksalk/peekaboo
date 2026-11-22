using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Isapi;

public sealed class IsapiAlarmLogClientTests
{
    [Fact]
    public async Task Search_follows_more_pages_and_advances_by_the_device_count()
    {
        var handler = new StubHandler((index, _) => index switch
        {
            0 => Xml(Result(
                "MORE",
                numOfMatches: 2,
                ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:00:00Z", "D1", "Pojazd"),
                ("log.hikvision.com/Alarm/motionStop/1", "2026-11-21T10:01:00Z", "D1", "Pojazd"))),
            1 => Xml(Result(
                "OK",
                numOfMatches: 2,
                ("log.hikvision.com/Alarm/motionStart/2", "2026-11-21T10:02:00Z", "D2", "Osoba"),
                ("log.hikvision.com/Alarm/motionStop/2", "2026-11-21T10:03:00Z", "D2", "Osoba"))),
            _ => throw new InvalidOperationException("Unexpected search page."),
        });

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Equal(4, result.Entries.Count);
        Assert.False(result.Truncated);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("<metaId>log.std-cgi.com/Alarm</metaId>", handler.Bodies[0]);
        Assert.Contains("<searchResultPostion>0</searchResultPostion>", handler.Bodies[0]);
        Assert.Contains("<searchResultPostion>2</searchResultPostion>", handler.Bodies[1]);
    }

    [Fact]
    public async Task Search_deduplicates_repeated_entries_and_sorts_by_time()
    {
        var handler = new StubHandler((index, _) => index switch
        {
            0 => Xml(Result(
                "MORE",
                numOfMatches: 1,
                ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:02:00Z", "D1", "Osoba"))),
            1 => Xml(Result(
                "OK",
                numOfMatches: 2,
                ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:02:00Z", "D1", "Osoba"),
                ("log.hikvision.com/Alarm/motionStop/1", "2026-11-21T10:03:00Z", "D1", "Osoba"))),
            _ => throw new InvalidOperationException("Unexpected search page."),
        });

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.Entries[0].Time < result.Entries[1].Time);
        Assert.Equal(IsapiAlarmLogKind.MotionStart, result.Entries[0].Kind);
        Assert.Equal(IsapiAlarmLogKind.MotionStop, result.Entries[1].Kind);
    }

    [Fact]
    public async Task Search_advances_by_raw_row_count_when_numOfMatches_is_missing()
    {
        var handler = new StubHandler((index, _) => index switch
        {
            0 => Xml(Result(
                "MORE",
                numOfMatches: 0,
                ("log.hikvision.com/Alarm/shelteralarm/1", "2026-11-21T10:00:00Z", "D1", "Tamper"),
                ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:01:00Z", "D1", "Osoba"))),
            1 => Xml(Result(
                "OK",
                numOfMatches: 1,
                ("log.hikvision.com/Alarm/motionStop/1", "2026-11-21T10:02:00Z", "D1", "Osoba"))),
            _ => throw new InvalidOperationException("Unexpected search page."),
        });

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Equal(2, result.Entries.Count);
        Assert.Contains("<searchResultPostion>2</searchResultPostion>", handler.Bodies[1]);
    }

    [Fact]
    public async Task Search_keeps_distinct_entries_with_the_same_timestamp_and_meta_id()
    {
        var handler = new StubHandler((_, _) => Xml(Result(
            "OK",
            numOfMatches: 2,
            ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:00:00Z", "D1", "Osoba"),
            ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:00:00Z", "D1", "Pojazd"))));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Equal(2, result.Entries.Count);
    }

    [Fact]
    public async Task Search_maps_an_error_status_to_a_device_error()
    {
        var handler = new StubHandler((_, _) => Xml(Result("ERROR", 0)));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchAsync(Request(), CancellationToken.None));

        Assert.Equal(IsapiFailureKind.DeviceError, exception.Kind);
        Assert.Contains("ERROR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_marks_results_truncated_when_the_page_limit_is_exhausted()
    {
        var baseTime = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
        var handler = new StubHandler((index, _) => Xml(Result(
            "MORE",
            numOfMatches: 1,
            (
                "log.hikvision.com/Alarm/motionStart/1",
                IsapiTime.FormatSearch(baseTime.AddSeconds(index)),
                "D1",
                "Osoba"))));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Equal(200, result.Entries.Count);
        Assert.True(result.Truncated);
        Assert.Equal(200, handler.Bodies.Count);
    }

    [Fact]
    public async Task Search_returns_an_empty_result_for_no_matches()
    {
        var handler = new StubHandler((_, _) => Xml(Result("NO MATCHES", 0)));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Empty(result.Entries);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Authentication_failures_are_not_returned_as_peekaboo_unauthorized()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchAsync(Request(), CancellationToken.None));

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
            client.SearchAsync(invalid, CancellationToken.None));

        Assert.Equal(IsapiFailureKind.BadRequest, exception.Kind);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task More_without_progress_is_reported_as_truncated()
    {
        var handler = new StubHandler((_, _) => Xml(Result(
            "MORE",
            numOfMatches: 0,
            ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T10:00:00Z", "D1", "Osoba"))));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var result = await client.SearchAsync(Request(), CancellationToken.None);

        Assert.Single(result.Entries);
        Assert.True(result.Truncated);
        Assert.Equal(2, handler.Bodies.Count);
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
            client.SearchAsync(Request(), CancellationToken.None));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task Malformed_responses_are_reported_as_invalid()
    {
        var handler = new StubHandler((_, _) => Xml("not xml"));

        using var http = new HttpClient(handler);
        var client = CreateClient(http);

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchAsync(Request(), CancellationToken.None));

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
            client.SearchAsync(Request(), CancellationToken.None));

        Assert.Equal(IsapiFailureKind.DeviceError, exception.Kind);
        Assert.Contains("Invalid Operation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_times_out_when_the_total_budget_is_exceeded()
    {
        using var http = new HttpClient(new DelayedHandler());
        var client = CreateClient(http, TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<IsapiException>(() =>
            client.SearchAsync(Request(), CancellationToken.None));

        Assert.Equal(IsapiFailureKind.Timeout, exception.Kind);
    }

    private static IsapiAlarmLogClient CreateClient(
        HttpClient http,
        TimeSpan? totalTimeout = null)
    {
        var resolver = new IsapiEndpointResolver(
            http,
            new IsapiOptions(8666, InsecureTls: false),
            NullLogger<IsapiEndpointResolver>.Instance);

        return new IsapiAlarmLogClient(
            http,
            resolver,
            NullLogger<IsapiAlarmLogClient>.Instance,
            totalTimeout);
    }

    private static IsapiAlarmLogRequest Request() =>
        new(
            "nvr-1",
            "rtsp://nvr.local:554",
            new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
        };

    private static string Result(
        string status,
        int numOfMatches,
        params (string MetaId, string Time, string LocalId, string Target)[] entries)
    {
        var matchList = new XElement("matchList");

        foreach (var (metaId, time, localId, target) in entries)
        {
            matchList.Add(
                new XElement(
                    "searchMatchItem",
                    new XElement(
                        "logDescriptor",
                        new XElement("metaId", metaId),
                        new XElement("StartDateTime", time),
                        new XElement("localId", localId),
                        new XElement("additionInformation", target))));
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

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
