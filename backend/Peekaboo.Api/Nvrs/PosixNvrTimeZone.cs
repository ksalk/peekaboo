using System.Globalization;
using System.Text.RegularExpressions;

namespace Peekaboo.Api.Nvrs;

/// <summary>
/// A DST-aware parser for the POSIX time-zone strings returned by Hikvision's
/// <c>/ISAPI/System/time</c> endpoint, for example
/// <c>CET-1CEST,M3.5.0/2,M10.5.0/3</c>.
/// </summary>
public sealed class PosixNvrTimeZone : INvrTimeZone
{
    private static readonly Regex ZonePattern = new(
        "^(?<std><[^>]+>|[A-Za-z]{2,})(?<stdOffset>[+-]?\\d{1,2}(?::\\d{1,2}){0,2})?" +
        "(?<dst><[^>]+>|[A-Za-z]{2,})?(?<dstOffset>[+-]?\\d{1,2}(?::\\d{1,2}){0,2})?" +
        "(?:,(?<start>[^,]+),(?<end>[^,]+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MonthWeekDayPattern = new(
        "^M(?<month>\\d{1,2})\\.(?<week>\\d)\\.(?<weekday>\\d)(?:/(?<time>.+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex JulianNoLeapPattern = new(
        "^J(?<day>\\d{1,3})(?:/(?<time>.+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DayOfYearPattern = new(
        "^(?<day>\\d{1,3})(?:/(?<time>.+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TimePattern = new(
        "^(?<hours>[+-]?\\d{1,3})(?::(?<minutes>\\d{1,2}))?(?::(?<seconds>\\d{1,2}))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly TimeSpan _standardOffset;
    private readonly TimeSpan? _daylightOffset;
    private readonly PosixTransition? _dstStart;
    private readonly PosixTransition? _dstEnd;

    private PosixNvrTimeZone(
        string id,
        TimeSpan standardOffset,
        TimeSpan? daylightOffset,
        PosixTransition? dstStart,
        PosixTransition? dstEnd)
    {
        Id = id;
        _standardOffset = standardOffset;
        _daylightOffset = daylightOffset;
        _dstStart = dstStart;
        _dstEnd = dstEnd;
    }

    public string Id { get; }

    public static bool TryParse(string? value, out PosixNvrTimeZone? timeZone)
    {
        timeZone = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var match = ZonePattern.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        if (!match.Groups["stdOffset"].Success ||
            !TryParsePosixOffset(match.Groups["stdOffset"].Value, out var standardOffset))
        {
            return false;
        }

        var hasDaylight = match.Groups["dst"].Success;
        TimeSpan? daylightOffset = null;
        PosixTransition? start = null;
        PosixTransition? end = null;

        if (hasDaylight)
        {
            if (!match.Groups["start"].Success || !match.Groups["end"].Success)
            {
                // A rule-less DST abbreviation has no portable meaning. Fall
                // back to the device's localTime offset instead of guessing.
                return false;
            }

            if (!TryParsePosixOffset(
                    match.Groups["dstOffset"].Value,
                    out var parsedDaylightOffset))
            {
                return false;
            }

            daylightOffset = match.Groups["dstOffset"].Success
                ? parsedDaylightOffset
                : standardOffset + TimeSpan.FromHours(1);

            if (!PosixTransition.TryParse(match.Groups["start"].Value, out start) ||
                !PosixTransition.TryParse(match.Groups["end"].Value, out end))
            {
                return false;
            }
        }
        else if (match.Groups["start"].Success || match.Groups["end"].Success)
        {
            return false;
        }

        timeZone = new PosixNvrTimeZone(
            trimmed,
            standardOffset,
            daylightOffset,
            start,
            end);
        return true;
    }

    public TimeSpan GetUtcOffset(DateTime utc)
    {
        if (_daylightOffset is null || _dstStart is null || _dstEnd is null)
        {
            return _standardOffset;
        }

        var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var start = _dstStart.Resolve(value.Year, _standardOffset).UtcDateTime;
        var end = _dstEnd.Resolve(value.Year, _daylightOffset.Value).UtcDateTime;

        if (start == end)
        {
            return _standardOffset;
        }

        var inDaylight = start < end
            ? value >= start && value < end
            : value >= start || value < end;

        return inDaylight ? _daylightOffset.Value : _standardOffset;
    }

    public DateTime ToDeviceWallTime(DateTimeOffset utc)
    {
        var offset = GetUtcOffset(utc.UtcDateTime);
        return DateTime.SpecifyKind(utc.UtcDateTime + offset, DateTimeKind.Unspecified);
    }

    public DateTimeOffset FromDeviceWallTime(DateTime local)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (_daylightOffset is null)
        {
            return new DateTimeOffset(wall, _standardOffset);
        }

        var standard = new DateTimeOffset(wall, _standardOffset);
        var daylight = new DateTimeOffset(wall, _daylightOffset.Value);

        var standardValid = GetUtcOffset(standard.UtcDateTime) == _standardOffset;
        var daylightValid = GetUtcOffset(daylight.UtcDateTime) == _daylightOffset.Value;

        if (standardValid && daylightValid)
        {
            // Clocks moved back: the wall time exists twice. Use the first
            // occurrence, which is the daylight-saving offset.
            return daylight;
        }

        if (standardValid)
        {
            return standard;
        }

        if (daylightValid)
        {
            return daylight;
        }

        // A wall time inside the spring-forward gap has no exact instant.
        // Keep it deterministic by applying the pre-transition offset.
        return standard;
    }

    private static bool TryParsePosixOffset(string? value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        var sign = 1;
        var index = 0;

        if (trimmed[0] == '+')
        {
            index = 1;
        }
        else if (trimmed[0] == '-')
        {
            sign = -1;
            index = 1;
        }

        var parts = trimmed[index..].Split(':');
        if (parts.Length is < 1 or > 3 ||
            !int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours) ||
            hours > 167)
        {
            return false;
        }

        var minutes = 0;
        var seconds = 0;

        if (parts.Length > 1 &&
            (!int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out minutes) ||
             minutes > 59))
        {
            return false;
        }

        if (parts.Length > 2 &&
            (!int.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out seconds) ||
             seconds > 59))
        {
            return false;
        }

        var magnitude = TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);

        // POSIX offsets use the opposite sign from ISO: CET-1 is UTC+1.
        offset = sign > 0 ? magnitude.Negate() : magnitude;
        return true;
    }

    private sealed record PosixTransition(
        PosixTransitionKind Kind,
        int Month,
        int Week,
        int Weekday,
        int Day,
        TimeSpan Time)
    {
        public static bool TryParse(string? value, out PosixTransition? transition)
        {
            transition = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            var match = MonthWeekDayPattern.Match(trimmed);

            if (match.Success)
            {
                var month = Number(match.Groups["month"].Value);
                var week = Number(match.Groups["week"].Value);
                var weekday = Number(match.Groups["weekday"].Value);
                var time = ParseTime(match.Groups["time"].Value);

                if (month is < 1 or > 12 ||
                    week is < 1 or > 5 ||
                    weekday is < 0 or > 6 ||
                    time is null)
                {
                    return false;
                }

                transition = new PosixTransition(
                    PosixTransitionKind.MonthWeekDay,
                    month,
                    week,
                    weekday,
                    0,
                    time.Value);
                return true;
            }

            match = JulianNoLeapPattern.Match(trimmed);
            if (match.Success)
            {
                var day = Number(match.Groups["day"].Value);
                var time = ParseTime(match.Groups["time"].Value);
                if (day is < 1 or > 365 || time is null)
                {
                    return false;
                }

                transition = new PosixTransition(
                    PosixTransitionKind.JulianNoLeap,
                    0,
                    0,
                    0,
                    day,
                    time.Value);
                return true;
            }

            match = DayOfYearPattern.Match(trimmed);
            if (match.Success)
            {
                var day = Number(match.Groups["day"].Value);
                var time = ParseTime(match.Groups["time"].Value);
                if (day is < 0 or > 365 || time is null)
                {
                    return false;
                }

                transition = new PosixTransition(
                    PosixTransitionKind.DayOfYear,
                    0,
                    0,
                    0,
                    day,
                    time.Value);
                return true;
            }

            return false;
        }

        public DateTimeOffset Resolve(int year, TimeSpan offsetBefore)
        {
            DateTime local;

            if (Kind == PosixTransitionKind.MonthWeekDay)
            {
                var first = new DateTime(year, Month, 1);
                var firstWeekday = (int)first.DayOfWeek;
                var dayOfMonth = 1 + ((Weekday - firstWeekday + 7) % 7) + (Week - 1) * 7;
                var daysInMonth = DateTime.DaysInMonth(year, Month);

                while (dayOfMonth > daysInMonth)
                {
                    dayOfMonth -= 7;
                }

                local = new DateTime(year, Month, dayOfMonth).Add(Time);
            }
            else
            {
                var daysToAdd = Kind == PosixTransitionKind.JulianNoLeap
                    ? Math.Clamp(Day, 1, 365) - 1
                    : Math.Clamp(Day, 0, 365);

                local = new DateTime(year, 1, 1).AddDays(daysToAdd);

                if (Kind == PosixTransitionKind.JulianNoLeap &&
                    DateTime.IsLeapYear(year) &&
                    Day >= 60)
                {
                    local = local.AddDays(1);
                }

                local = local.Add(Time);
            }

            return new DateTimeOffset(local, offsetBefore);
        }

        private static int Number(string value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;

        private static TimeSpan? ParseTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TimeSpan.FromHours(2);
            }

            var match = TimePattern.Match(value.Trim());
            if (!match.Success)
            {
                return null;
            }

            var hours = Number(match.Groups["hours"].Value);
            var minutes = match.Groups["minutes"].Success
                ? Number(match.Groups["minutes"].Value)
                : 0;
            var seconds = match.Groups["seconds"].Success
                ? Number(match.Groups["seconds"].Value)
                : 0;

            if (hours is < -167 or > 167 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
            {
                return null;
            }

            var sign = hours < 0 ? -1 : 1;
            return new TimeSpan(hours, sign * minutes, sign * seconds);
        }
    }

    private enum PosixTransitionKind
    {
        MonthWeekDay,
        JulianNoLeap,
        DayOfYear,
    }
}
