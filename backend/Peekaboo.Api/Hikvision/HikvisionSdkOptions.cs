using System.Globalization;

namespace Peekaboo.Api.Hikvision;

public sealed record HikvisionSdkOptions(
    string? SdkPath,
    uint ConnectTimeoutMs,
    uint ReconnectIntervalMs,
    uint LogLevel,
    string? LogDirectory)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SdkPath);

    public static HikvisionSdkOptions FromConfiguration(IConfiguration configuration)
    {
        var sdkPath = NullIfWhiteSpace(configuration["PEEKABOO_HIK_SDK_PATH"]);
        var logDirectory = NullIfWhiteSpace(configuration["PEEKABOO_HIK_SDK_LOG_DIR"]);
        var connectTimeout = ParseBounded(
            configuration["PEEKABOO_HIK_SDK_CONNECT_TIMEOUT_MS"],
            defaultValue: 3000,
            minimum: 1,
            maximum: 60_000,
            "PEEKABOO_HIK_SDK_CONNECT_TIMEOUT_MS");
        var reconnectInterval = ParseBounded(
            configuration["PEEKABOO_HIK_SDK_RECONNECT_MS"],
            defaultValue: 10_000,
            minimum: 1,
            maximum: 300_000,
            "PEEKABOO_HIK_SDK_RECONNECT_MS");
        var logLevel = ParseBounded(
            configuration["PEEKABOO_HIK_SDK_LOG_LEVEL"],
            defaultValue: 0,
            minimum: 0,
            maximum: 3,
            "PEEKABOO_HIK_SDK_LOG_LEVEL");

        return new HikvisionSdkOptions(
            sdkPath,
            (uint)connectTimeout,
            (uint)reconnectInterval,
            (uint)logLevel,
            logDirectory);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseBounded(
        string? value,
        int defaultValue,
        int minimum,
        int maximum,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be an integer between {minimum} and {maximum}.");
        }

        return parsed;
    }
}
