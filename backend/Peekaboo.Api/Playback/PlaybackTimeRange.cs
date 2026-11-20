using System.Globalization;

namespace Peekaboo.Api.Playback;

public static class PlaybackTimeRange
{
    public const int MaximumDays = 31;

    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "O",
    ];

    public static bool TryParse(
        string? startValue,
        string? endValue,
        out DateTimeOffset start,
        out DateTimeOffset end,
        out string? error)
    {
        start = default;
        end = default;
        error = null;

        if (!TryParseTime(startValue, out start) || !TryParseTime(endValue, out end))
        {
            error = "Valid ISO 8601 start and end times are required.";
            return false;
        }

        if (end <= start)
        {
            error = "The recording end must be after its start.";
            return false;
        }

        if (end - start > TimeSpan.FromDays(MaximumDays))
        {
            error = $"A recording search can span at most {MaximumDays} days.";
            return false;
        }

        return true;
    }

    private static bool TryParseTime(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParseExact(
            value?.Trim(),
            IsoFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
}
