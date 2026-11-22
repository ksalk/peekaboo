using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

internal sealed record IsapiSearchPage(
    string Status,
    int NumOfMatches,
    IReadOnlyList<IsapiRecordingSegment> Segments,
    string? DeviceError);

internal static class IsapiSearchXml
{
    public static string Build(
        string searchId,
        string trackId,
        DateTimeOffset start,
        DateTimeOffset end,
        INvrTimeZone timeZone,
        int maxResults,
        int position)
    {
        var deviceStart = timeZone.ToDeviceWallTime(start);
        var deviceEnd = timeZone.ToDeviceWallTime(end);

        var document = new XDocument(
            new XElement(
                "CMSearchDescription",
                new XElement("searchID", searchId),
                new XElement(
                    "trackIDList",
                    new XElement("trackID", trackId)),
                new XElement(
                    "timeSpanList",
                    new XElement(
                        "timeSpan",
                        new XElement("startTime", IsapiTime.FormatSearch(deviceStart)),
                        new XElement("endTime", IsapiTime.FormatSearch(deviceEnd)))),
                new XElement("maxResults", maxResults.ToString(CultureInfo.InvariantCulture)),
                new XElement("searchResultPostion", position.ToString(CultureInfo.InvariantCulture)),
                new XElement(
                    "metadataList",
                    new XElement("metadataDescriptor", "//recordType.meta.std-cgi.com"))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static IsapiSearchPage Parse(string xml, INvrTimeZone timeZone)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml.TrimStart('\uFEFF'));
        }
        catch (XmlException)
        {
            throw new IsapiException(
                "The NVR returned an invalid XML response.",
                IsapiFailureKind.InvalidResponse);
        }

        var root = document.Root
            ?? throw new IsapiException(
                "The NVR response was empty.",
                IsapiFailureKind.InvalidResponse);

        var searchResult = root
            .DescendantsAndSelf()
            .FirstOrDefault(element => string.Equals(
                element.Name.LocalName,
                "CMSearchResult",
                StringComparison.OrdinalIgnoreCase));
        var responseStatus = searchResult is null
            ? root.DescendantsAndSelf().FirstOrDefault(element => string.Equals(
                element.Name.LocalName,
                "ResponseStatus",
                StringComparison.OrdinalIgnoreCase))
            : null;

        if (searchResult is null && responseStatus is null)
        {
            throw new IsapiException(
                "The NVR response did not contain a recording search result.",
                IsapiFailureKind.InvalidResponse);
        }

        if (responseStatus is not null)
        {
            return new IsapiSearchPage(
                "ERROR",
                0,
                [],
                DeviceError(responseStatus));
        }

        if (string.Equals(
                ChildValue(searchResult, "responseStatus")?.Trim(),
                "false",
                StringComparison.OrdinalIgnoreCase))
        {
            return new IsapiSearchPage(
                "ERROR",
                0,
                [],
                DeviceError(searchResult!));
        }

        var status = ChildValue(searchResult, "responseStatusStrg")?.Trim().ToUpperInvariant()
            ?? "OK";
        var numOfMatches = ParseInt(ChildValue(searchResult, "numOfMatches"));
        var segments = new List<IsapiRecordingSegment>();

        var items = Descendants(searchResult!, "searchMatchItem").ToList();
        if (items.Count == 0)
        {
            items = Descendants(searchResult!, "matchElement").ToList();
        }

        foreach (var item in items)
        {
            var timeSpan = Child(item, "timeSpan");
            var start = IsapiTime.Parse(ChildValue(timeSpan, "startTime"), timeZone);
            var end = IsapiTime.Parse(ChildValue(timeSpan, "endTime"), timeZone);

            if (start is null || end is null || end <= start)
            {
                continue;
            }

            var descriptor = Child(item, "mediaSegmentDescriptor");
            var contentType = ChildValue(descriptor, "contentType");

            if (!string.IsNullOrEmpty(contentType) &&
                !string.Equals(contentType, "video", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            segments.Add(new IsapiRecordingSegment(
                start.Value,
                end.Value,
                ChildValue(descriptor, "codecType")));
        }

        return new IsapiSearchPage(status, numOfMatches, segments, DeviceError: null);
    }

    private static string DeviceError(XElement element) =>
        ChildValue(element, "statusString")
        ?? ChildValue(element, "subStatusCode")
        ?? ChildValue(element, "statusCode")
        ?? "The NVR rejected the recording search.";

    private static int ParseInt(string? value) =>
        int.TryParse(
            value?.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;

    private static IEnumerable<XElement> Descendants(XContainer parent, string name) =>
        parent.Descendants()
            .Where(element => string.Equals(
                element.Name.LocalName,
                name,
                StringComparison.OrdinalIgnoreCase));

    private static XElement? Child(XContainer? parent, string name) =>
        parent?.Elements().FirstOrDefault(element => string.Equals(
            element.Name.LocalName,
            name,
            StringComparison.OrdinalIgnoreCase));

    private static string? ChildValue(XContainer? parent, string name) =>
        Child(parent, name)?.Value;
}
