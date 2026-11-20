using System.Globalization;

namespace Peekaboo.Api.Isapi;

public sealed record IsapiOptions(int? HttpPort, bool InsecureTls)
{
    public static IsapiOptions FromConfiguration(IConfiguration configuration)
    {
        var portValue = configuration["PEEKABOO_ISAPI_HTTP_PORT"]?.Trim();
        int? httpPort = null;

        if (!string.IsNullOrEmpty(portValue))
        {
            if (!int.TryParse(
                    portValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPort) ||
                parsedPort is < 1 or > 65535)
            {
                throw new InvalidOperationException(
                    "PEEKABOO_ISAPI_HTTP_PORT must be an integer between 1 and 65535.");
            }

            httpPort = parsedPort;
        }

        var insecureValue = configuration["PEEKABOO_ISAPI_INSECURE_TLS"]?.Trim();
        var insecureTls = false;

        if (!string.IsNullOrEmpty(insecureValue) &&
            !bool.TryParse(insecureValue, out insecureTls))
        {
            throw new InvalidOperationException(
                "PEEKABOO_ISAPI_INSECURE_TLS must be 'true' or 'false'.");
        }

        return new IsapiOptions(httpPort, insecureTls);
    }
}
