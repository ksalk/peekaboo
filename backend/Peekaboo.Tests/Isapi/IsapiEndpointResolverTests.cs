using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Isapi;
using Xunit;

namespace Peekaboo.Tests.Isapi;

public sealed class IsapiEndpointResolverTests
{
    [Fact]
    public async Task Prefers_http_when_it_responds()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Scheme == Uri.UriSchemeHttp
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : throw new HttpRequestException("Connection refused"));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://192.168.1.64:554",
            CancellationToken.None);

        Assert.Equal("http://192.168.1.64", endpoint);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Falls_back_to_https_when_http_is_unreachable()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Scheme == Uri.UriSchemeHttps
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : throw new HttpRequestException("Connection refused"));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);

        Assert.Equal("https://nvr.local", endpoint);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Authentication_challenge_still_means_the_endpoint_is_reachable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);

        Assert.Equal("http://nvr.local", endpoint);
    }

    [Fact]
    public async Task Configured_http_port_skips_probing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Should not be called"));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http, new IsapiOptions(8666, InsecureTls: false));

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://192.168.1.64:554",
            CancellationToken.None);

        Assert.Equal("http://192.168.1.64:8666", endpoint);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Resolution_is_cached_until_invalidated()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var first = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);
        var second = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, handler.Calls);

        resolver.Invalidate("nvr-1", "rtsp://nvr.local:554");
        await resolver.ResolveAsync("nvr-1", "rtsp://nvr.local:554", CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Failed_discovery_reports_both_endpoints()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Network unreachable"));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var exception = await Assert.ThrowsAsync<IsapiEndpointException>(() =>
            resolver.ResolveAsync("nvr-1", "rtsp://nvr.local:554", CancellationToken.None));

        Assert.Contains("Could not reach", exception.Message, StringComparison.Ordinal);
        Assert.Contains("http://nvr.local", exception.Message, StringComparison.Ordinal);
        Assert.Contains("https://nvr.local", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_host_redirect_is_used_as_the_endpoint()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.Scheme == Uri.UriSchemeHttp)
            {
                var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                response.Headers.Location = new Uri("https://nvr.local/ISAPI/System/deviceInfo");
                return response;
            }

            throw new HttpRequestException("Should not be called");
        });

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);

        Assert.Equal("https://nvr.local", endpoint);
    }

    [Fact]
    public async Task Missing_device_info_endpoint_falls_back_to_https()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Scheme == Uri.UriSchemeHttp
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);

        Assert.Equal("https://nvr.local", endpoint);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Server_error_does_not_win_endpoint_discovery()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Scheme == Uri.UriSchemeHttp
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://nvr.local:554",
            CancellationToken.None);

        Assert.Equal("https://nvr.local", endpoint);
    }

    [Fact]
    public async Task Cross_host_redirect_is_rejected()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.Scheme == Uri.UriSchemeHttp)
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("https://evil.test/ISAPI/System/deviceInfo");
                return response;
            }

            throw new HttpRequestException("Connection refused");
        });

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var exception = await Assert.ThrowsAsync<IsapiEndpointException>(() =>
            resolver.ResolveAsync("nvr-1", "rtsp://nvr.local:554", CancellationToken.None));

        Assert.Contains("unexpected address", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cache_is_scoped_by_nvr_and_host()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        await resolver.ResolveAsync("nvr-1", "rtsp://nvr.local:554", CancellationToken.None);
        await resolver.ResolveAsync("nvr-2", "rtsp://nvr.local:554", CancellationToken.None);
        await resolver.ResolveAsync("nvr-1", "rtsp://other.local:554", CancellationToken.None);

        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData("rtsp://192.168.1.64:554", "192.168.1.64")]
    [InlineData("rtsps://nvr.local:322", "nvr.local")]
    [InlineData("rtsp://[::1]:554", "[::1]")]
    public void DeriveHost_returns_the_host_without_the_rtsp_port(
        string baseUrl,
        string expectedHost)
    {
        Assert.Equal(expectedHost, IsapiEndpointResolver.DeriveHost(baseUrl));
    }

    [Fact]
    public void DeriveHost_rejects_invalid_urls()
    {
        Assert.Throws<ArgumentException>(() => IsapiEndpointResolver.DeriveHost("not a url"));
    }

    [Fact]
    public async Task Ipv6_hosts_are_preserved()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(handler);
        var resolver = CreateResolver(http);

        var endpoint = await resolver.ResolveAsync(
            "nvr-1",
            "rtsp://[::1]:554",
            CancellationToken.None);

        Assert.Equal("http://[::1]", endpoint);
    }

    [Fact]
    public void Defaults_disable_overrides()
    {
        var options = IsapiOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Null(options.HttpPort);
        Assert.False(options.InsecureTls);
    }

    [Fact]
    public void Options_parse_valid_values()
    {
        var options = IsapiOptions.FromConfiguration(Configuration(
            ("PEEKABOO_ISAPI_HTTP_PORT", "8666"),
            ("PEEKABOO_ISAPI_INSECURE_TLS", "true")));

        Assert.Equal(8666, options.HttpPort);
        Assert.True(options.InsecureTls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void Invalid_http_port_is_rejected(string value)
    {
        var configuration = Configuration(("PEEKABOO_ISAPI_HTTP_PORT", value));

        Assert.Throws<InvalidOperationException>(() => IsapiOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void Invalid_tls_setting_is_rejected()
    {
        var configuration = Configuration(("PEEKABOO_ISAPI_INSECURE_TLS", "sometimes"));

        Assert.Throws<InvalidOperationException>(() => IsapiOptions.FromConfiguration(configuration));
    }

    private static IsapiEndpointResolver CreateResolver(
        HttpClient http,
        IsapiOptions? options = null) =>
        new(
            http,
            options ?? new IsapiOptions(HttpPort: null, InsecureTls: false),
            NullLogger<IsapiEndpointResolver>.Instance);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
