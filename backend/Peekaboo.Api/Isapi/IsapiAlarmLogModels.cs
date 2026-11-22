using Peekaboo.Api.Nvrs;

namespace Peekaboo.Api.Isapi;

public enum IsapiAlarmLogKind
{
    MotionStart,
    MotionStop,
}

public sealed record IsapiAlarmLogRequest(
    string NvrId,
    string RtspBaseUrl,
    DateTimeOffset Start,
    DateTimeOffset End,
    INvrTimeZone TimeZone);

public sealed record IsapiAlarmLogEntry(
    IsapiAlarmLogKind Kind,
    DateTimeOffset Time,
    int ChannelId,
    string? TargetLabel,
    string MetaId,
    string Fingerprint = "");

public sealed record IsapiAlarmLogResult(
    IReadOnlyList<IsapiAlarmLogEntry> Entries,
    bool Truncated);

public interface IIsapiAlarmLogClient
{
    Task<IsapiAlarmLogResult> SearchAsync(
        IsapiAlarmLogRequest request,
        CancellationToken cancellationToken);
}
