using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

internal sealed record IsapiAlarmLogPage(
    string Status,
    int NumOfMatches,
    int RowCount,
    IReadOnlyList<IsapiAlarmLogEntry> Entries,
    string? DeviceError);

internal static class IsapiAlarmLogXml
{
    public const string AlarmMetaId = "log.std-cgi.com/Alarm";

    public static string Build(
        string searchId,
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
                new XAttribute("version", "1.0"),
                new XElement("searchID", searchId),
                new XElement(
                    "timeSpanList",
                    new XElement(
                        "timeSpan",
                        new XElement("startTime", IsapiTime.FormatSearch(deviceStart)),
                        new XElement("endTime", IsapiTime.FormatSearch(deviceEnd)))),
                new XElement("metaId", AlarmMetaId),
                new XElement("maxResults", maxResults.ToString(CultureInfo.InvariantCulture)),
                // Hikvision firmware expects this misspelling.
                new XElement(
                    "searchResultPostion",
                    position.ToString(CultureInfo.InvariantCulture))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static IsapiAlarmLogPage Parse(string xml, INvrTimeZone timeZone)
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
                "The NVR response did not contain a log search result.",
                IsapiFailureKind.InvalidResponse);
        }

        if (responseStatus is not null)
        {
            return new IsapiAlarmLogPage(
                "ERROR",
                0,
                0,
                [],
                DeviceError(responseStatus));
        }

        var status = ChildValue(searchResult, "responseStatusStrg")?.Trim().ToUpperInvariant()
            ?? "OK";

        if (string.Equals(
                ChildValue(searchResult, "responseStatus")?.Trim(),
                "false",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(status, "NO MATCHES", StringComparison.Ordinal))
            {
                return new IsapiAlarmLogPage(
                    "NO MATCHES",
                    0,
                    0,
                    [],
                    DeviceError: null);
            }

            return new IsapiAlarmLogPage(
                "ERROR",
                0,
                0,
                [],
                DeviceError(searchResult!));
        }

        if (status is not ("OK" or "MORE" or "NO MATCHES"))
        {
            return new IsapiAlarmLogPage(
                "ERROR",
                0,
                0,
                [],
                Truncate($"The NVR rejected the log search: {status}"));
        }

        var numOfMatches = ParseInt(ChildValue(searchResult, "numOfMatches"));
        var items = Descendants(searchResult!, "searchMatchItem").ToList();
        var entries = new List<IsapiAlarmLogEntry>();

        foreach (var item in items)
        {
            var descriptor = Child(item, "logDescriptor");
            if (descriptor is null)
            {
                continue;
            }

            if (ParseEntry(descriptor, timeZone) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return new IsapiAlarmLogPage(
            status,
            numOfMatches,
            items.Count,
            entries,
            DeviceError: null);
    }

    private static IsapiAlarmLogEntry? ParseEntry(
        XElement descriptor,
        INvrTimeZone timeZone)
    {
        var metaId = ChildValue(descriptor, "metaId")?.Trim();
        var time = IsapiTime.Parse(ChildValue(descriptor, "StartDateTime"), timeZone);
        if (string.IsNullOrEmpty(metaId) || time is null)
        {
            return null;
        }

        var segments = metaId.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var alarmIndex = Array.FindIndex(
            segments,
            segment => string.Equals(segment, "Alarm", StringComparison.OrdinalIgnoreCase));
        if (alarmIndex < 0 || alarmIndex + 1 >= segments.Length)
        {
            return null;
        }

        var kind = segments[alarmIndex + 1].ToLowerInvariant() switch
        {
            "motionstart" => IsapiAlarmLogKind.MotionStart,
            "motionstop" => IsapiAlarmLogKind.MotionStop,
            _ => (IsapiAlarmLogKind?)null,
        };
        if (kind is null)
        {
            return null;
        }

        var channelId = alarmIndex + 2 < segments.Length &&
            int.TryParse(
                segments[alarmIndex + 2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedChannel)
            ? parsedChannel
            : ParseLocalChannel(ChildValue(descriptor, "localId") ??
                                ChildValue(descriptor, "localID"));

        if (channelId < 1)
        {
            return null;
        }

        var target = ChildValue(descriptor, "additionInformation")?.Trim();
        var fingerprint = string.Join(
            '\u001f',
            descriptor.Elements().Select(element =>
                $"{element.Name.LocalName}={element.Value.Trim()}"));

        return new IsapiAlarmLogEntry(
            kind.Value,
            time.Value,
            channelId,
            string.IsNullOrEmpty(target) ? null : target,
            metaId,
            fingerprint);
    }

    private static int ParseLocalChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var digits = value.Trim().TrimStart('D', 'd').Trim();
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var channel)
            ? channel
            : 0;
    }

    private static string DeviceError(XElement element) =>
        Truncate(
            ChildValue(element, "statusString")
            ?? ChildValue(element, "subStatusCode")
            ?? ChildValue(element, "statusCode")
            ?? "The NVR rejected the log search.");

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

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
