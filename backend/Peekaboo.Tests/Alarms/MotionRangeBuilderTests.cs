using Peekaboo.Api.Alarms;
using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Alarms;

public sealed class MotionRangeBuilderTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_pairs_starts_and_stops_by_channel_and_target()
    {
        var ranges = MotionRangeBuilder.Build(
            [
                Start(1, 0, "Osoba"),
                Stop(1, 7, "Osoba"),
                Start(2, 10, "Pojazd"),
                Stop(2, 18, "Pojazd"),
            ],
            BaseTime.AddHours(-1),
            BaseTime.AddHours(1));

        Assert.Equal(2, ranges.Count);
        Assert.Equal(AlarmTarget.Human, ranges[0].Target);
        Assert.Equal(BaseTime.AddSeconds(7), ranges[0].End);
        Assert.Equal(AlarmTarget.Vehicle, ranges[1].Target);
        Assert.Equal(2, ranges[1].ChannelId);
    }

    [Fact]
    public void Build_closes_overlapping_same_target_starts_in_fifo_order()
    {
        var ranges = MotionRangeBuilder.Build(
            [
                Start(1, 0, "Osoba"),
                Start(1, 5, "Osoba"),
                Stop(1, 10, "Osoba"),
                Stop(1, 20, "Osoba"),
            ],
            BaseTime.AddHours(-1),
            BaseTime.AddHours(1));

        Assert.Equal(2, ranges.Count);
        Assert.Equal(BaseTime.AddSeconds(10), ranges[0].End);
        Assert.Equal(BaseTime.AddSeconds(20), ranges[1].End);
    }

    [Fact]
    public void Build_falls_back_to_another_target_when_the_stop_label_differs()
    {
        var ranges = MotionRangeBuilder.Build(
            [
                Start(1, 0, "Osoba"),
                Stop(1, 8, "Pojazd"),
            ],
            BaseTime.AddHours(-1),
            BaseTime.AddHours(1));

        var range = Assert.Single(ranges);
        Assert.Equal(AlarmTarget.Human, range.Target);
        Assert.Equal("Osoba", range.TargetLabel);
    }

    [Fact]
    public void Build_closes_an_unmatched_start_at_the_query_end()
    {
        var ranges = MotionRangeBuilder.Build(
            [Start(1, 0, "Osoba")],
            BaseTime.AddHours(-1),
            BaseTime.AddHours(1));

        var range = Assert.Single(ranges);
        Assert.True(range.InferredEnd);
        Assert.Equal(BaseTime.AddHours(1), range.End);
    }

    [Fact]
    public void Build_caps_an_unmatched_start_at_the_query_end()
    {
        var ranges = MotionRangeBuilder.Build(
            [Start(1, 0, "Osoba")],
            BaseTime.AddHours(-1),
            BaseTime.AddSeconds(30));

        var range = Assert.Single(ranges);
        Assert.True(range.InferredEnd);
        Assert.Equal(BaseTime.AddSeconds(30), range.End);
    }

    [Fact]
    public void Build_keeps_a_stop_without_a_start_as_an_inferred_start_range()
    {
        var ranges = MotionRangeBuilder.Build(
            [Stop(1, 8, "Osoba")],
            BaseTime.AddMinutes(-10),
            BaseTime.AddHours(1));

        var range = Assert.Single(ranges);
        Assert.True(range.InferredStart);
        Assert.False(range.InferredEnd);
        Assert.Equal(BaseTime.AddMinutes(-10), range.Start);
        Assert.Equal(BaseTime.AddSeconds(8), range.End);
    }

    [Theory]
    [InlineData("Osoba", AlarmTarget.Human)]
    [InlineData("Person", AlarmTarget.Human)]
    [InlineData("Pojazd", AlarmTarget.Vehicle)]
    [InlineData("Vehicle", AlarmTarget.Vehicle)]
    [InlineData("Person detected", AlarmTarget.Human)]
    [InlineData("Osoba wykryta", AlarmTarget.Human)]
    [InlineData("automatic", AlarmTarget.Unknown)]
    [InlineData("carpark", AlarmTarget.Unknown)]
    [InlineData("Tamper", AlarmTarget.Unknown)]
    [InlineData(null, AlarmTarget.Unknown)]
    public void Alarm_targets_normalize_nvr_labels(string? label, AlarmTarget expected)
    {
        Assert.Equal(expected, AlarmTargets.Parse(label));
    }

    private static IsapiAlarmLogEntry Start(int channel, int seconds, string? target) =>
        new(
            IsapiAlarmLogKind.MotionStart,
            BaseTime.AddSeconds(seconds),
            channel,
            target,
            $"log.hikvision.com/Alarm/motionStart/{channel}");

    private static IsapiAlarmLogEntry Stop(int channel, int seconds, string? target) =>
        new(
            IsapiAlarmLogKind.MotionStop,
            BaseTime.AddSeconds(seconds),
            channel,
            target,
            $"log.hikvision.com/Alarm/motionStop/{channel}");
}
