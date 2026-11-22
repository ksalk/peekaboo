using Peekaboo.Api.Alarms;
using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Alarms;

public sealed class AlarmRangeServiceCacheTests
{
    [Fact]
    public void Cache_size_is_proportional_to_parsed_entries()
    {
        Assert.Equal(
            1,
            AlarmRangeService.CacheSize(new IsapiAlarmLogResult([], Truncated: false)));

        var entries = Enumerable.Range(0, 100)
            .Select(index => new IsapiAlarmLogEntry(
                IsapiAlarmLogKind.MotionStart,
                DateTimeOffset.UnixEpoch.AddSeconds(index),
                1,
                "Osoba",
                $"log.hikvision.com/Alarm/motionStart/1/{index}"))
            .ToList();

        Assert.Equal(
            100,
            AlarmRangeService.CacheSize(new IsapiAlarmLogResult(entries, Truncated: false)));
    }
}
