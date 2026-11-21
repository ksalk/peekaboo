using Peekaboo.Api.Hikvision;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

public sealed class HikvisionTimeTests
{
    [Fact]
    public void V50_times_are_utc_with_an_explicit_zero_offset()
    {
        var value = new DateTimeOffset(2026, 11, 20, 10, 0, 1, 250, TimeSpan.Zero);

        var time = HikvisionTime.ToV50(value);

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
    public void Non_utc_input_is_converted_to_utc_before_formatting()
    {
        var value = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2));

        var time = HikvisionTime.ToV50(value);

        Assert.Equal(10, time.byHour);
        Assert.Equal(1, time.byISO8601);
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

        var parsed = HikvisionTime.FromSearch(value);

        Assert.Equal(TimeSpan.FromHours(1), parsed.Offset);
        Assert.Equal(2026, parsed.Year);
        Assert.Equal(11, parsed.Hour);
        Assert.Equal(500, parsed.Millisecond);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(500),
            parsed.ToUniversalTime());
    }

    [Fact]
    public void Search_results_without_an_offset_are_treated_as_utc()
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

        var parsed = HikvisionTime.FromSearch(value);

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(11, parsed.Hour);
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

        var exception = Assert.Throws<HikvisionException>(
            () => HikvisionTime.FromSearch(value));

        Assert.Equal(HikvisionFailureKind.Device, exception.Kind);
    }

    [Fact]
    public void Search_conditions_mark_the_time_as_utc()
    {
        var time = HikvisionTime.ToSearchCond(
            new DateTimeOffset(2026, 11, 20, 23, 59, 59, TimeSpan.Zero));

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
}
