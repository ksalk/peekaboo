using System.Net;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Isapi;

/// <summary>
/// Long-lived HTTP client for NVR ISAPI calls. Credentials are attached only in
/// response to an authentication challenge and never written to URLs or logs.
/// </summary>
public sealed class IsapiHttpClient : IDisposable
{
    private readonly HttpClient _client;

    public IsapiHttpClient(IsapiOptions options, CameraCredentials credentials)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        if (credentials.CreateCredential() is { } credential)
        {
            handler.Credentials = credential;
        }

        if (options.InsecureTls)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                static (_, _, _, _) => true;
        }

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public HttpClient Client => _client;

    public void Dispose() => _client.Dispose();
}
