using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Hikvision;

/// <summary>
/// Converts between true UTC instants and the V50 SDK time structures. Hikvision
/// firmware reads and writes device-local wall-clock fields; the explicit ISO
/// flag plus the time-zone offset makes the represented instant unambiguous.
///
/// The non-UTC encoding (device-local fields + a non-zero offset) is deliberately
/// documented here because it needs verification against a real NVR. The prior
/// UTC-fields/zero-offset scheme was verified on hardware; the new scheme is the
/// consistent extension of the ISAPI device-local timestamp behavior.
/// </summary>
internal static class HikvisionTime
{
    public static NetDvrTimeV50 ToV50(DateTimeOffset value, INvrTimeZone timeZone)
    {
        var device = timeZone.ToDeviceWallTime(value);
        var offset = timeZone.GetUtcOffset(value.UtcDateTime);

        return new NetDvrTimeV50
        {
            wYear = (ushort)device.Year,
            byMonth = (byte)device.Month,
            byDay = (byte)device.Day,
            byHour = (byte)device.Hour,
            byMinute = (byte)device.Minute,
            bySecond = (byte)device.Second,
            byISO8601 = 1,
            wMillisecond = (ushort)device.Millisecond,
            cTimeDifferenceH = (sbyte)offset.Hours,
            cTimeDifferenceM = (sbyte)offset.Minutes,
        };
    }

    public static DateTimeOffset FromSearch(NetDvrTimeSearch value, INvrTimeZone timeZone)
    {
        if (value.byLocalOrUTC != 1)
        {
            try
            {
                var local = new DateTime(
                    value.wYear,
                    value.byMonth,
                    value.byDay,
                    value.byHour,
                    value.byMinute,
                    value.bySecond,
                    DateTimeKind.Unspecified);

                return timeZone.FromDeviceWallTime(local).ToUniversalTime();
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new HikvisionException(
                    "The NVR returned an invalid recording timestamp.",
                    HikvisionFailureKind.Device,
                    innerException: exception);
            }
        }

        var offsetMinutes = (value.cTimeDifferenceH * 60) + value.cTimeDifferenceM;
        offsetMinutes = Math.Clamp(offsetMinutes, -14 * 60, 14 * 60);

        try
        {
            return new DateTimeOffset(
                    value.wYear,
                    value.byMonth,
                    value.byDay,
                    value.byHour,
                    value.byMinute,
                    value.bySecond,
                    TimeSpan.FromMinutes(offsetMinutes))
                .AddMilliseconds(value.wMillisecond)
                .ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new HikvisionException(
                "The NVR returned an invalid recording timestamp.",
                HikvisionFailureKind.Device,
                innerException: exception);
        }
    }

    public static NetDvrTimeSearchCond ToSearchCond(
        DateTimeOffset value,
        INvrTimeZone timeZone)
    {
        var device = timeZone.ToDeviceWallTime(value);
        var offset = timeZone.GetUtcOffset(value.UtcDateTime);

        return new NetDvrTimeSearchCond
        {
            wYear = (ushort)device.Year,
            byMonth = (byte)device.Month,
            byDay = (byte)device.Day,
            byHour = (byte)device.Hour,
            byMinute = (byte)device.Minute,
            bySecond = (byte)device.Second,
            byLocalOrUTC = 1,
            wMillisecond = (ushort)device.Millisecond,
            cTimeDifferenceH = (sbyte)offset.Hours,
            cTimeDifferenceM = (sbyte)offset.Minutes,
        };
    }
}
