namespace Peekaboo.Api.Nvrs;

/// <summary>
/// Converts between true UTC instants and the wall-clock time used by an NVR.
/// Hikvision firmware commonly reports device-local wall time even when the
/// payload is suffixed with <c>Z</c>, so services must not assume that an
/// unlabelled device timestamp is UTC.
/// </summary>
public interface INvrTimeZone
{
    string Id { get; }

    TimeSpan GetUtcOffset(DateTime utc);

    DateTime ToDeviceWallTime(DateTimeOffset utc);

    DateTimeOffset FromDeviceWallTime(DateTime local);
}

public sealed class FixedNvrTimeZone(string id, TimeSpan offset) : INvrTimeZone
{
    public string Id { get; } = id;

    public TimeSpan GetUtcOffset(DateTime utc) => offset;

    public DateTime ToDeviceWallTime(DateTimeOffset utc) =>
        DateTime.SpecifyKind(utc.UtcDateTime + offset, DateTimeKind.Unspecified);

    public DateTimeOffset FromDeviceWallTime(DateTime local) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
}

public sealed class TimeZoneInfoNvrTimeZone(TimeZoneInfo zone) : INvrTimeZone
{
    public string Id => zone.Id;

    public TimeSpan GetUtcOffset(DateTime utc) =>
        zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    public DateTime ToDeviceWallTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, zone);

    public DateTimeOffset FromDeviceWallTime(DateTime local)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset;

        if (zone.IsInvalidTime(wall))
        {
            // A wall time inside the spring-forward gap has no real instant.
            // Use the offset in effect immediately after the gap so the value
            // still maps forward rather than throwing.
            offset = zone.GetUtcOffset(wall.AddHours(1));
        }
        else if (zone.IsAmbiguousTime(wall))
        {
            // Prefer the first occurrence, which is the daylight-saving offset.
            offset = zone.GetAmbiguousTimeOffsets(wall).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(wall);
        }

        return new DateTimeOffset(wall, offset);
    }
}
