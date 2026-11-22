using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

public sealed class HikvisionTimeTests
{
    [Fact]
    public void V50_times_are_utc_with_an_explicit_zero_offset()
    {
        var value = new DateTimeOffset(2026, 11, 20, 10, 0, 1, 250, TimeSpan.Zero);

        var time = HikvisionTime.ToV50(value, TestNvrTimeZones.Utc);

        Assert.Equal(2026, time.wYear);
        Assert.Equal(11, time.byMonth);
        Assert.Equal(20, time.byDay);
        Assert.Equal(10, time.byHour);
        Assert.Equal(0, time.byMinute);
        Assert.Equal(1, time.bySecond);
        Assert.Equal(250, time.wMillisecond);
        Assert.Equal(1, time.byISO8601);
        Assert.Equal(0, time.cTimeDifferenceH);
        Assert.Equal(0, time.cTimeDifferenceM);
    }

    [Fact]
    public void V50_times_use_the_device_wall_clock_and_offset()
    {
        var value = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
        var zone = new TimeZoneInfoNvrTimeZone(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));

        var time = HikvisionTime.ToV50(value, zone);

        Assert.Equal(12, time.byHour);
        Assert.Equal(1, time.byISO8601);
        Assert.Equal(2, time.cTimeDifferenceH);
        Assert.Equal(0, time.cTimeDifferenceM);
    }

    [Fact]
    public void Search_results_apply_the_device_offset_and_milliseconds()
    {
        var value = new NetDvrTimeSearch
        {
            wYear = 2026,
            byMonth = 11,
            byDay = 20,
            byHour = 11,
            byMinute = 0,
            bySecond = 0,
            cTimeDifferenceH = 1,
            cTimeDifferenceM = 0,
            byLocalOrUTC = 1,
            wMillisecond = 500,
        };

        var parsed = HikvisionTime.FromSearch(value, TestNvrTimeZones.Utc);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(500),
            parsed);
    }

    [Fact]
    public void Search_results_without_an_offset_use_the_resolved_device_zone()
    {
        var value = new NetDvrTimeSearch
        {
            wYear = 2026,
            byMonth = 11,
            byDay = 20,
            byHour = 11,
            byMinute = 0,
            bySecond = 0,
            cTimeDifferenceH = 8,
            byLocalOrUTC = 0,
        };

        var parsed = HikvisionTime.FromSearch(
            value,
            new FixedNvrTimeZone("CET", TimeSpan.FromHours(1)));

        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero),
            parsed);
    }

    [Fact]
    public void Search_results_without_an_offset_and_utc_zone_are_utc()
    {
        var value = new NetDvrTimeSearch
        {
            wYear = 2026,
            byMonth = 11,
            byDay = 20,
            byHour = 11,
            byMinute = 0,
            bySecond = 0,
            cTimeDifferenceH = 8,
            byLocalOrUTC = 0,
        };

        var parsed = HikvisionTime.FromSearch(value, TestNvrTimeZones.Utc);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero),
            parsed);
    }

    [Fact]
    public void Invalid_search_result_timestamps_throw_a_device_error()
    {
        var value = new NetDvrTimeSearch
        {
            wYear = 2026,
            byMonth = 13,
            byDay = 20,
            byHour = 11,
        };

        var exception = Assert.Throws<HikvisionException>(() =>
            HikvisionTime.FromSearch(value, TestNvrTimeZones.Utc));

        Assert.Equal(HikvisionFailureKind.Device, exception.Kind);
    }

    [Fact]
    public void Search_conditions_use_utc_for_a_utc_zone()
    {
        var time = HikvisionTime.ToSearchCond(
            new DateTimeOffset(2026, 11, 20, 23, 59, 59, TimeSpan.Zero),
            TestNvrTimeZones.Utc);

        Assert.Equal(2026, time.wYear);
        Assert.Equal(11, time.byMonth);
        Assert.Equal(20, time.byDay);
        Assert.Equal(23, time.byHour);
        Assert.Equal(59, time.byMinute);
        Assert.Equal(59, time.bySecond);
        Assert.Equal(1, time.byLocalOrUTC);
        Assert.Equal(0, time.cTimeDifferenceH);
        Assert.Equal(0, time.cTimeDifferenceM);
    }

    [Fact]
    public void Search_conditions_use_the_device_wall_clock_and_offset()
    {
        var time = HikvisionTime.ToSearchCond(
            new DateTimeOffset(2026, 11, 20, 23, 59, 59, TimeSpan.Zero),
            new FixedNvrTimeZone("CET", TimeSpan.FromHours(1)));

        Assert.Equal(21, time.byDay);
        Assert.Equal(0, time.byHour);
        Assert.Equal(59, time.byMinute);
        Assert.Equal(59, time.bySecond);
        Assert.Equal(1, time.byLocalOrUTC);
        Assert.Equal(1, time.cTimeDifferenceH);
        Assert.Equal(0, time.cTimeDifferenceM);
    }

    [Fact]
    public void V50_and_search_conditions_encode_a_negative_sub_hour_offset()
    {
        var value = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var zone = new FixedNvrTimeZone(
            "NST",
            TimeSpan.FromHours(-3) - TimeSpan.FromMinutes(30));

        var v50 = HikvisionTime.ToV50(value, zone);
        var condition = HikvisionTime.ToSearchCond(value, zone);

        Assert.Equal(8, v50.byHour);
        Assert.Equal(30, v50.byMinute);
        Assert.Equal(-3, v50.cTimeDifferenceH);
        Assert.Equal(-30, v50.cTimeDifferenceM);

        Assert.Equal(8, condition.byHour);
        Assert.Equal(30, condition.byMinute);
        Assert.Equal(-3, condition.cTimeDifferenceH);
        Assert.Equal(-30, condition.cTimeDifferenceM);
    }
}
