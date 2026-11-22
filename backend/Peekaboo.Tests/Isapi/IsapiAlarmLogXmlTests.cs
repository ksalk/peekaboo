using System.Xml.Linq;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Isapi;

public sealed class IsapiAlarmLogXmlTests
{
    private static readonly INvrTimeZone Utc = TestNvrTimeZones.Utc;
    [Fact]
    public void Build_writes_the_alarm_log_search_document()
    {
        var start = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.FromHours(1));
        var end = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.FromHours(1));

        var xml = IsapiAlarmLogXml.Build("SEARCH-1", start, end, Utc, 100, 64);
        var document = XDocument.Parse(xml);

        Assert.Equal("CMSearchDescription", document.Root!.Name.LocalName);
        Assert.Equal("SEARCH-1", document.Root.Element("searchID")!.Value);
        Assert.Equal("log.std-cgi.com/Alarm", document.Root.Element("metaId")!.Value);
        Assert.Equal("100", document.Root.Element("maxResults")!.Value);
        Assert.Equal("64", document.Root.Element("searchResultPostion")!.Value);
        Assert.Equal(
            "2026-11-20T23:00:00Z",
            document.Root.Element("timeSpanList")!
                .Element("timeSpan")!
                .Element("startTime")!
                .Value);
    }

    [Fact]
    public void Parse_reads_motion_start_and_stop_entries()
    {
        var page = Parse(Result(
            "OK",
            2,
            ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T08:44:55Z", "D1", "Pojazd"),
            ("log.hikvision.com/Alarm/motionStop/2", "2026-11-21T08:45:02Z", "D2", "Osoba")));

        Assert.Equal("OK", page.Status);
        Assert.Equal(2, page.NumOfMatches);
        Assert.Null(page.DeviceError);
        Assert.Equal(2, page.Entries.Count);
        Assert.Equal(IsapiAlarmLogKind.MotionStart, page.Entries[0].Kind);
        Assert.Equal(1, page.Entries[0].ChannelId);
        Assert.Equal("Pojazd", page.Entries[0].TargetLabel);
        Assert.Equal(IsapiAlarmLogKind.MotionStop, page.Entries[1].Kind);
        Assert.Equal(2, page.Entries[1].ChannelId);
    }

    [Fact]
    public void Parse_reads_namespaced_entries()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" ?>
            <CMSearchResult version="1.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
              <responseStatus>true</responseStatus>
              <responseStatusStrg>OK</responseStatusStrg>
              <numOfMatches>1</numOfMatches>
              <matchList>
                <searchMatchItem>
                  <logDescriptor>
                    <metaId>log.hikvision.com/Alarm/motionStart/2</metaId>
                    <StartDateTime>2026-11-21T08:44:55Z</StartDateTime>
                    <localId>D2</localId>
                    <additionInformation>Osoba</additionInformation>
                  </logDescriptor>
                </searchMatchItem>
              </matchList>
            </CMSearchResult>
            """;

        var entry = Assert.Single(Parse(xml).Entries);
        Assert.Equal(2, entry.ChannelId);
        Assert.Equal(IsapiAlarmLogKind.MotionStart, entry.Kind);
        Assert.Equal("Osoba", entry.TargetLabel);
    }

    [Fact]
    public void Parse_ignores_other_alarm_types()
    {
        var page = Parse(Result(
            "OK",
            2,
            ("log.hikvision.com/Alarm/shelteralarm/1", "2026-11-21T08:44:55Z", "D1", "Tamper"),
            ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T08:45:00Z", "D1", "Osoba")));

        var entry = Assert.Single(page.Entries);
        Assert.Equal(IsapiAlarmLogKind.MotionStart, entry.Kind);
    }

    [Fact]
    public void Parse_uses_the_local_channel_when_meta_id_has_no_channel()
    {
        var page = Parse(Result(
            "OK",
            1,
            ("log.hikvision.com/Alarm/motionStart", "2026-11-21T08:45:00Z", "D3", null)));

        Assert.Equal(3, Assert.Single(page.Entries).ChannelId);
    }

    [Fact]
    public void Parse_handles_no_matches()
    {
        var page = Parse(Result("NO MATCHES", 0));

        Assert.Empty(page.Entries);
        Assert.Null(page.DeviceError);
    }

    [Fact]
    public void Parse_counts_rows_before_filtering_to_motion()
    {
        var page = Parse(Result(
            "MORE",
            2,
            ("log.hikvision.com/Alarm/shelteralarm/1", "2026-11-21T08:44:55Z", "D1", "Tamper"),
            ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T08:45:00Z", "D1", "Osoba")));

        Assert.Equal(2, page.RowCount);
        Assert.Single(page.Entries);
    }

    [Fact]
    public void Parse_treats_false_with_no_matches_as_an_empty_result()
    {
        const string xml = """
            <CMSearchResult>
              <responseStatus>false</responseStatus>
              <responseStatusStrg>NO MATCHES</responseStatusStrg>
              <numOfMatches>0</numOfMatches>
            </CMSearchResult>
            """;

        var page = Parse(xml);

        Assert.Equal("NO MATCHES", page.Status);
        Assert.Empty(page.Entries);
        Assert.Null(page.DeviceError);
    }

    [Theory]
    [InlineData("ERROR")]
    [InlineData("FAILED")]
    public void Parse_treats_an_unknown_status_as_a_device_error(string status)
    {
        var page = Parse(Result(status, 0));

        Assert.Equal("ERROR", page.Status);
        Assert.NotNull(page.DeviceError);
    }

    [Fact]
    public void Parse_surfaces_device_errors()
    {
        const string xml = """
            <ResponseStatus version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
              <statusCode>4</statusCode>
              <statusString>Invalid Operation</statusString>
              <subStatusCode>lowPrivilege</subStatusCode>
            </ResponseStatus>
            """;

        var page = Parse(xml);

        Assert.Equal("ERROR", page.Status);
        Assert.Equal("Invalid Operation", page.DeviceError);
    }

    [Fact]
    public void Parse_rejects_malformed_xml()
    {
        Assert.Throws<IsapiException>(() => Parse("<not-xml"));
    }

    [Fact]
    public void Build_uses_device_wall_time_for_a_non_utc_zone()
    {
        var start = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
        var zone = new FixedNvrTimeZone("CET", TimeSpan.FromHours(1));

        var xml = IsapiAlarmLogXml.Build("SEARCH-1", start, end, zone, 100, 0);
        var document = XDocument.Parse(xml);

        Assert.Equal(
            "2026-11-21T01:00:00Z",
            document.Root!.Element("timeSpanList")!
                .Element("timeSpan")!
                .Element("startTime")!
                .Value);
    }

    [Fact]
    public void Parse_interprets_z_suffix_as_device_local_time()
    {
        var page = IsapiAlarmLogXml.Parse(
            Result(
                "OK",
                2,
                ("log.hikvision.com/Alarm/motionStart/1", "2026-11-21T09:44:55Z", "D1", "Osoba"),
                ("log.hikvision.com/Alarm/motionStop/1", "2026-11-21T09:45:02Z", "D1", "Osoba")),
            new FixedNvrTimeZone("CET", TimeSpan.FromHours(1)));

        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 8, 44, 55, TimeSpan.Zero),
            page.Entries[0].Time);
    }

    [Fact]
    public void Parse_skips_a_timestamp_that_overflows_the_zone_boundary()
    {
        var page = IsapiAlarmLogXml.Parse(
            Result(
                "OK",
                1,
                ("log.hikvision.com/Alarm/motionStart/1", "0001-01-01T00:00:00Z", "D1", "Osoba")),
            new FixedNvrTimeZone("CET", TimeSpan.FromHours(1)));

        Assert.Empty(page.Entries);
    }

    private static IsapiAlarmLogPage Parse(string xml) =>
        IsapiAlarmLogXml.Parse(xml, Utc);

    private static string Result(
        string status,
        int numOfMatches,
        params (string MetaId, string Time, string LocalId, string? Target)[] entries)
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
                        new XElement("additionInformation", target ?? string.Empty))));
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
}
