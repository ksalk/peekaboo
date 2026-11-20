using System.Globalization;

namespace Peekaboo.Api.Isapi;

/// <summary>
/// Hikvision uses ISO 8601 in search documents and compact UTC timestamps in
/// playback URLs. Both are always formatted from UTC.
/// </summary>
public static class IsapiTime
{
    public const string SearchFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    public const string PlaybackFormat = "yyyyMMdd'T'HHmmss'Z'";

    public static string FormatSearch(DateTimeOffset value) =>
        value.UtcDateTime.ToString(SearchFormat, CultureInfo.InvariantCulture);

    public static string FormatPlayback(DateTimeOffset value) =>
        value.UtcDateTime.ToString(PlaybackFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
