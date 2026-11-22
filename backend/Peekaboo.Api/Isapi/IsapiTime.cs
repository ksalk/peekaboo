using System.Globalization;
using System.Text.RegularExpressions;
using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

/// <summary>
/// Hikvision uses ISO 8601 in search documents and compact timestamps in
/// playback URLs. Real firmware commonly writes device-local wall-clock time
/// with a literal <c>Z</c>, so callers must supply the resolved NVR time zone
/// instead of assuming the suffix means UTC.
/// </summary>
public static class IsapiTime
{
    public const string SearchFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    public const string PlaybackFormat = "yyyyMMdd'T'HHmmss'Z'";

    private static readonly string[] WallFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
    ];

    private static readonly Regex ExplicitOffsetPattern = new(
        "[+-]\\d{2}:?\\d{2}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string FormatSearch(DateTime deviceWallTime) =>
        deviceWallTime.ToString(SearchFormat, CultureInfo.InvariantCulture);

    public static string FormatPlayback(DateTime deviceWallTime) =>
        deviceWallTime.ToString(PlaybackFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset? Parse(string? value, INvrTimeZone timeZone)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (ExplicitOffsetPattern.IsMatch(trimmed))
        {
            return DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var explicitValue)
                ? explicitValue.ToUniversalTime()
                : null;
        }

        var wallText = trimmed.EndsWith('Z') || trimmed.EndsWith('z')
            ? trimmed[..^1]
            : trimmed;

        if (!DateTime.TryParseExact(
                wallText,
                WallFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var wallTime))
        {
            return null;
        }

        try
        {
            return timeZone.FromDeviceWallTime(wallTime).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            // A device can return a wall clock at the DateTime boundary that
            // becomes unrepresentable once a non-zero offset is applied.
            return null;
        }
    }
}
