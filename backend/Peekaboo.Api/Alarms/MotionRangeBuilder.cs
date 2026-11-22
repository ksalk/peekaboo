using Peekaboo.Api.Isapi;

namespace Peekaboo.Api.Alarms;

public enum AlarmTarget
{
    Unknown,
    Human,
    Vehicle,
}

public static class AlarmTargets
{
    public static AlarmTarget Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AlarmTarget.Unknown;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var tokens = normalized.Split(
            [' ', '\t', '_', '-', '/', '.', ',', ';', ':', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Any(token => token is "osoba" or "person" or "human" or
                "człowiek" or "czlowiek"))
        {
            return AlarmTarget.Human;
        }

        if (tokens.Any(token => token is "pojazd" or "vehicle" or "car" or
                "samochód" or "samochod" or "auto"))
        {
            return AlarmTarget.Vehicle;
        }

        return AlarmTarget.Unknown;
    }

    public static string ToStorageValue(AlarmTarget target) =>
        target switch
        {
            AlarmTarget.Human => "human",
            AlarmTarget.Vehicle => "vehicle",
            _ => "unknown",
        };

    public static string Label(AlarmTarget target) =>
        target switch
        {
            AlarmTarget.Human => "Person",
            AlarmTarget.Vehicle => "Vehicle",
            _ => "Motion",
        };
}

public sealed record MotionRange(
    int ChannelId,
    DateTimeOffset Start,
    DateTimeOffset End,
    AlarmTarget Target,
    string? TargetLabel,
    bool InferredStart,
    bool InferredEnd);

public static class MotionRangeBuilder
{
    public static IReadOnlyList<MotionRange> Build(
        IReadOnlyList<IsapiAlarmLogEntry> entries,
        DateTimeOffset queryStart,
        DateTimeOffset queryEnd)
    {
        var ranges = new List<MotionRange>();

        foreach (var channel in entries.GroupBy(entry => entry.ChannelId))
        {
            var open = new Dictionary<AlarmTarget, Queue<IsapiAlarmLogEntry>>();

            foreach (var entry in channel.OrderBy(entry => entry.Time).ThenBy(entry => entry.MetaId))
            {
                if (entry.Kind == IsapiAlarmLogKind.MotionStart)
                {
                    var target = AlarmTargets.Parse(entry.TargetLabel);
                    if (!open.TryGetValue(target, out var queue))
                    {
                        queue = new Queue<IsapiAlarmLogEntry>();
                        open[target] = queue;
                    }

                    queue.Enqueue(entry);
                    continue;
                }

                var start = TakeStart(open, AlarmTargets.Parse(entry.TargetLabel));
                if (start is null)
                {
                    // The motion started before the expanded query window. Keep
                    // the stop as a lower-bound range rather than dropping it.
                    if (queryStart < entry.Time)
                    {
                        ranges.Add(new MotionRange(
                            channel.Key,
                            queryStart,
                            entry.Time,
                            AlarmTargets.Parse(entry.TargetLabel),
                            entry.TargetLabel,
                            InferredStart: true,
                            InferredEnd: false));
                    }

                    continue;
                }

                ranges.Add(new MotionRange(
                    channel.Key,
                    start.Time,
                    entry.Time,
                    AlarmTargets.Parse(start.TargetLabel),
                    start.TargetLabel,
                    InferredStart: false,
                    InferredEnd: false));
            }

            foreach (var pair in open)
            {
                while (pair.Value.Count > 0)
                {
                    var start = pair.Value.Dequeue();
                    if (queryEnd <= start.Time)
                    {
                        continue;
                    }

                    ranges.Add(new MotionRange(
                        channel.Key,
                        start.Time,
                        queryEnd,
                        pair.Key,
                        start.TargetLabel,
                        InferredStart: false,
                        InferredEnd: true));
                }
            }
        }

        return ranges
            .OrderBy(range => range.Start)
            .ThenBy(range => range.End)
            .ThenBy(range => range.ChannelId)
            .ToList();
    }

    private static IsapiAlarmLogEntry? TakeStart(
        Dictionary<AlarmTarget, Queue<IsapiAlarmLogEntry>> open,
        AlarmTarget preferred)
    {
        if (open.TryGetValue(preferred, out var queue) && queue.Count > 0)
        {
            return queue.Dequeue();
        }

        if (preferred != AlarmTarget.Unknown &&
            open.TryGetValue(AlarmTarget.Unknown, out var unknown) &&
            unknown.Count > 0)
        {
            return unknown.Dequeue();
        }

        IsapiAlarmLogEntry? earliest = null;
        AlarmTarget? earliestKey = null;

        foreach (var pair in open)
        {
            if (pair.Value.Count == 0)
            {
                continue;
            }

            var candidate = pair.Value.Peek();
            if (earliest is null || candidate.Time < earliest.Time)
            {
                earliest = candidate;
                earliestKey = pair.Key;
            }
        }

        if (earliest is not null && earliestKey is { } key)
        {
            open[key].Dequeue();
        }

        return earliest;
    }
}
