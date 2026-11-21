namespace Peekaboo.Api.Hikvision;

/// <summary>
/// Converts UTC instants into the V50 time structures. The login uses
/// byUseUTCTime=0; the explicit ISO-8601 flag plus a zero offset tells the NVR
/// that the supplied fields are UTC. This combination was verified against the
/// real NVR (10:00 UTC played back as 11:00 local at +01:00).
/// </summary>
internal static class HikvisionTime
{
    public static NetDvrTimeV50 ToV50(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;

        return new NetDvrTimeV50
        {
            wYear = (ushort)utc.Year,
            byMonth = (byte)utc.Month,
            byDay = (byte)utc.Day,
            byHour = (byte)utc.Hour,
            byMinute = (byte)utc.Minute,
            bySecond = (byte)utc.Second,
            byISO8601 = 1,
            wMillisecond = (ushort)utc.Millisecond,
            cTimeDifferenceH = 0,
            cTimeDifferenceM = 0,
        };
    }

    public static NetDvrTimeSearchCond ToSearchCond(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;

        return new NetDvrTimeSearchCond
        {
            wYear = (ushort)utc.Year,
            byMonth = (byte)utc.Month,
            byDay = (byte)utc.Day,
            byHour = (byte)utc.Hour,
            byMinute = (byte)utc.Minute,
            bySecond = (byte)utc.Second,
            byLocalOrUTC = 1,
            wMillisecond = (ushort)utc.Millisecond,
            cTimeDifferenceH = 0,
            cTimeDifferenceM = 0,
        };
    }
}
