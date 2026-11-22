using System.Xml.Linq;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Isapi;

public sealed class IsapiSearchXmlTests
{
    private static readonly INvrTimeZone Utc = TestNvrTimeZones.Utc;
    [Fact]
    public void Build_writes_the_hikvision_search_document()
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 32, 1, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 20, 10, 40, 42, TimeSpan.Zero);

        var xml = IsapiSearchXml.Build("SEARCH-1", "101", start, end, Utc, 100, 40);
        var document = XDocument.Parse(xml);

        Assert.Equal("CMSearchDescription", document.Root!.Name.LocalName);
        Assert.Equal("SEARCH-1", document.Root.Element("searchID")!.Value);
        Assert.Equal("101", document.Root.Element("trackIDList")!.Element("trackID")!.Value);
        Assert.Equal(
            "2026-11-20T10:32:01Z",
            document.Root.Element("timeSpanList")!
                .Element("timeSpan")!
                .Element("startTime")!
                .Value);
        Assert.Equal("40", document.Root.Element("searchResultPostion")!.Value);
        Assert.Equal(
            "//recordType.meta.std-cgi.com",
            document.Root.Element("metadataList")!
                .Element("metadataDescriptor")!
                .Value);
    }

    [Fact]
    public void Parse_reads_namespaced_matches()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <CMSearchResult version="2.0" xmlns="http://www.isapi.com/ver20/XMLSchema">
              <searchID>{C77384AD-66A0-0001-E7C2-1151F04F90B0}</searchID>
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>2</numOfMatches>
              <matchList>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>2017-03-14T10:32:01Z</startTime>
                    <endTime>2017-03-14T10:40:42Z</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <contentType>video</contentType>
                    <codecType>H.264-BP</codecType>
                    <playbackURI>rtsp://10.17.132.49/Streaming/tracks/101/?starttime=20170314T103201Z&amp;endtime=20170314T104042Z</playbackURI>
                  </mediaSegmentDescriptor>
                </searchMatchItem>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>2017-03-14T10:40:42Z</startTime>
                    <endTime>2017-03-14T10:53:14Z</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <contentType>video</contentType>
                    <codecType>H.264-BP</codecType>
                  </mediaSegmentDescriptor>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;

        var page = Parse(xml);

        Assert.Equal("OK", page.Status);
        Assert.Equal(2, page.NumOfMatches);
        Assert.Null(page.DeviceError);
        Assert.Equal(2, page.Segments.Count);
        Assert.Equal(
            new DateTimeOffset(2017, 3, 14, 10, 32, 1, TimeSpan.Zero),
            page.Segments[0].Start);
        Assert.Equal("H.264-BP", page.Segments[0].CodecType);
    }

    [Fact]
    public void Parse_reports_more_results()
    {
        var page = Parse(Result("MORE", 29));

        Assert.Equal("MORE", page.Status);
        Assert.Equal(29, page.NumOfMatches);
    }

    [Fact]
    public void Parse_handles_no_matches()
    {
        var page = Parse(Result("NO MATCHES", 0));

        Assert.Equal("NO MATCHES", page.Status);
        Assert.Empty(page.Segments);
        Assert.Null(page.DeviceError);
    }

    [Fact]
    public void Parse_surfaces_device_errors()
    {
        const string xml = """
            <ResponseStatus version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
              <statusCode>4</statusCode>
              <statusString>Invalid Operation</statusString>
              <subStatusCode>invalidOperation</subStatusCode>
            </ResponseStatus>
            """;

        var page = Parse(xml);

        Assert.Equal("ERROR", page.Status);
        Assert.Equal("Invalid Operation", page.DeviceError);
    }

    [Fact]
    public void Parse_handles_whitespace_around_response_status()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus> false </responseStatus>
              <statusString>Denied</statusString>
            </CMSearchResult>
            """;

        var page = Parse(xml);

        Assert.Equal("ERROR", page.Status);
        Assert.Equal("Denied", page.DeviceError);
    }

    [Fact]
    public void Parse_strips_a_utf8_bom()
    {
        var page = Parse("\uFEFF" + Result("OK", 0));

        Assert.Equal("OK", page.Status);
        Assert.Null(page.DeviceError);
    }

    [Fact]
    public void Parse_reads_the_alternate_match_element_shape()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>1</numOfMatches>
              <matchList>
                <matchElement>
                  <timeSpan>
                    <startTime>2026-11-20T10:00:00Z</startTime>
                    <endTime>2026-11-20T10:05:00Z</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <codecType>H.264-BP</codecType>
                  </mediaSegmentDescriptor>
                </matchElement>
              </matchList>
            </CMSearchResult>
            """;

        var page = Parse(xml);

        var segment = Assert.Single(page.Segments);
        Assert.Equal("H.264-BP", segment.CodecType);
    }

    [Fact]
    public void Parse_skips_picture_matches()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>1</numOfMatches>
              <matchList>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>2026-11-20T10:00:00Z</startTime>
                    <endTime>2026-11-20T10:05:00Z</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <contentType>picture</contentType>
                  </mediaSegmentDescriptor>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;

        var page = Parse(xml);

        Assert.Empty(page.Segments);
    }

    [Fact]
    public void Build_uses_device_wall_time_for_a_non_utc_zone()
    {
        var start = new DateTimeOffset(2026, 11, 20, 9, 32, 1, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 20, 9, 40, 42, TimeSpan.Zero);
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        var xml = IsapiSearchXml.Build("SEARCH-1", "101", start, end, zone, 100, 0);
        var document = XDocument.Parse(xml);

        Assert.Equal(
            "2026-11-20T10:32:01Z",
            document.Root!.Element("timeSpanList")!
                .Element("timeSpan")!
                .Element("startTime")!
                .Value);
        Assert.Equal(
            "2026-11-20T10:40:42Z",
            document.Root.Element("timeSpanList")!
                .Element("timeSpan")!
                .Element("endTime")!
                .Value);
    }

    [Fact]
    public void Parse_interprets_z_suffix_as_device_local_time()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>1</numOfMatches>
              <matchList>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>2026-11-20T10:32:01Z</startTime>
                    <endTime>2026-11-20T10:40:42Z</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <contentType>video</contentType>
                  </mediaSegmentDescriptor>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        var page = IsapiSearchXml.Parse(xml, zone);

        var segment = Assert.Single(page.Segments);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 9, 32, 1, TimeSpan.Zero),
            segment.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 9, 40, 42, TimeSpan.Zero),
            segment.End);
    }

    [Fact]
    public void Parse_honours_an_explicit_offset_before_the_device_zone()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>1</numOfMatches>
              <matchList>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>2026-11-20T10:32:01+01:00</startTime>
                    <endTime>2026-11-20T10:40:42+01:00</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <contentType>video</contentType>
                  </mediaSegmentDescriptor>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        var page = IsapiSearchXml.Parse(xml, zone);

        var segment = Assert.Single(page.Segments);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 9, 32, 1, TimeSpan.Zero),
            segment.Start);
    }

    [Fact]
    public void Parse_skips_a_timestamp_that_overflows_the_zone_boundary()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>1</numOfMatches>
              <matchList>
                <searchMatchItem>
                  <timeSpan>
                    <startTime>0001-01-01T00:00:00Z</startTime>
                    <endTime>0001-01-01T00:00:05Z</endTime>
                  </timeSpan>
                  <mediaSegmentDescriptor>
                    <contentType>video</contentType>
                  </mediaSegmentDescriptor>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        var page = IsapiSearchXml.Parse(xml, zone);

        Assert.Empty(page.Segments);
    }

    [Fact]
    public void Parse_rejects_malformed_xml()
    {
        Assert.Throws<IsapiException>(() => Parse("<not-xml"));
    }

    private static IsapiSearchPage Parse(string xml) =>
        IsapiSearchXml.Parse(xml, Utc);

    private static string Result(string status, int numOfMatches) =>
        new XDocument(
            new XElement(
                "CMSearchResult",
                new XElement("responseStatus", "true"),
                new XElement("responseStatusStrg", status),
                new XElement("numOfMatches", numOfMatches)))
            .ToString(SaveOptions.DisableFormatting);
}
