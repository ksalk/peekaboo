using Peekaboo.Api.Nvrs;

namespace Peekaboo.Tests;

internal static class TestNvrTimeZones
{
    public static INvrTimeZone Utc { get; } =
        new FixedNvrTimeZone("UTC", TimeSpan.Zero);

    public static INvrTimeZone Warsaw { get; } =
        new TimeZoneInfoNvrTimeZone(
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
}
