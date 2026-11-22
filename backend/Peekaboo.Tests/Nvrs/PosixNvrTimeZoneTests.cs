using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Nvrs;

public sealed class PosixNvrTimeZoneTests
{
    [Fact]
    public void Parses_a_fixed_posix_zone()
    {
        Assert.True(PosixNvrTimeZone.TryParse("CST-8", out var zone));
        Assert.NotNull(zone);
        Assert.Equal(TimeSpan.FromHours(8), zone.GetUtcOffset(new DateTime(2026, 11, 22)));
        Assert.Equal(
            new DateTime(2026, 11, 22, 20, 0, 0),
            zone.ToDeviceWallTime(
                new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Parses_utc_without_an_offset()
    {
        Assert.True(PosixNvrTimeZone.TryParse("UTC0", out var zone));
        Assert.NotNull(zone);
        Assert.Equal(TimeSpan.Zero, zone.GetUtcOffset(new DateTime(2026, 11, 22)));
    }

    [Fact]
    public void Parses_northern_hemisphere_dst_rules()
    {
        Assert.True(
            PosixNvrTimeZone.TryParse(
                "CET-1CEST,M3.5.0/2,M10.5.0/3",
                out var zone));
        Assert.NotNull(zone);

        Assert.Equal(
            TimeSpan.FromHours(1),
            zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0)));
        Assert.Equal(
            TimeSpan.FromHours(2),
            zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0)));

        // Europe/Warsaw: last Sunday of March and October in 2026.
        Assert.Equal(
            TimeSpan.FromHours(1),
            zone.GetUtcOffset(new DateTime(2026, 3, 29, 0, 59, 0)));
        Assert.Equal(
            TimeSpan.FromHours(2),
            zone.GetUtcOffset(new DateTime(2026, 3, 29, 1, 0, 0)));
        Assert.Equal(
            TimeSpan.FromHours(2),
            zone.GetUtcOffset(new DateTime(2026, 10, 25, 0, 59, 0)));
        Assert.Equal(
            TimeSpan.FromHours(1),
            zone.GetUtcOffset(new DateTime(2026, 10, 25, 1, 0, 0)));
    }

    [Fact]
    public void Converts_wall_time_both_ways()
    {
        Assert.True(
            PosixNvrTimeZone.TryParse(
                "CET-1CEST,M3.5.0/2,M10.5.0/3",
                out var zone));
        Assert.NotNull(zone);

        var converted = zone.FromDeviceWallTime(new DateTime(2026, 7, 15, 14, 30, 0));

        Assert.Equal(TimeSpan.FromHours(2), converted.Offset);
        Assert.Equal(
            new DateTime(2026, 7, 15, 12, 30, 0, DateTimeKind.Utc),
            converted.UtcDateTime);
        Assert.Equal(
            new DateTime(2026, 7, 15, 14, 30, 0),
            zone.ToDeviceWallTime(converted));
    }

    [Fact]
    public void Uses_the_first_occurrence_for_an_ambiguous_wall_time()
    {
        Assert.True(
            PosixNvrTimeZone.TryParse(
                "CET-1CEST,M3.5.0/2,M10.5.0/3",
                out var zone));
        Assert.NotNull(zone);

        var converted = zone.FromDeviceWallTime(new DateTime(2026, 10, 25, 2, 30, 0));

        Assert.Equal(TimeSpan.FromHours(2), converted.Offset);
        Assert.Equal(
            new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc),
            converted.UtcDateTime);
    }

    [Fact]
    public void Parses_southern_hemisphere_dst_rules()
    {
        Assert.True(
            PosixNvrTimeZone.TryParse(
                "AEST-10AEDT,M10.1.0/2,M4.1.0/3",
                out var zone));
        Assert.NotNull(zone);

        Assert.Equal(
            TimeSpan.FromHours(11),
            zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0)));
        Assert.Equal(
            TimeSpan.FromHours(10),
            zone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0)));

        // DST starts at 02:00 standard time on the first Sunday of October.
        Assert.Equal(
            TimeSpan.FromHours(10),
            zone.GetUtcOffset(new DateTime(2026, 10, 3, 15, 59, 0)));
        Assert.Equal(
            TimeSpan.FromHours(11),
            zone.GetUtcOffset(new DateTime(2026, 10, 3, 16, 0, 0)));
    }

    [Fact]
    public void Parses_julian_and_day_of_year_rules()
    {
        Assert.True(
            PosixNvrTimeZone.TryParse(
                "CET-1CEST,J60/2,J300/3",
                out var julianZone));
        Assert.NotNull(julianZone);
        Assert.Equal(
            TimeSpan.FromHours(2),
            julianZone.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0)));

        Assert.True(
            PosixNvrTimeZone.TryParse(
                "CET-1CEST,59/2,299/3",
                out var dayZone));
        Assert.NotNull(dayZone);
        Assert.Equal(
            TimeSpan.FromHours(2),
            dayZone.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a zone")]
    [InlineData("CET")]
    [InlineData("CEST")]
    [InlineData("EST5EDT")]
    [InlineData("CET-1CEST,M13.5.0/2,M10.5.0/3")]
    [InlineData("CET-1CEST,M3.9.0/2,M10.5.0/3")]
    [InlineData("CET-1CEST,M3.5.9/2,M10.5.0/3")]
    [InlineData("CET-1CEST,not-a-rule,M10.5.0/3")]
    public void Rejects_invalid_zones(string value)
    {
        Assert.False(PosixNvrTimeZone.TryParse(value, out _));
    }

    [Fact]
    public void Time_zone_info_wrapper_handles_ambiguous_times()
    {
        var zone = new TimeZoneInfoNvrTimeZone(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));

        var converted = zone.FromDeviceWallTime(new DateTime(2026, 10, 25, 2, 30, 0));

        Assert.Equal(TimeSpan.FromHours(2), converted.Offset);
        Assert.Equal(
            new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc),
            converted.UtcDateTime);
    }
}
