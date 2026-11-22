using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Xunit;

namespace Peekaboo.Tests.Nvrs;

public sealed class NvrClockResolverTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Detects_a_posix_time_zone_from_the_nvr()
    {
        var handler = new StubHandler(
            """
            <Time>
              <timeMode>manual</timeMode>
              <localTime>2026-11-22T13:00:00+01:00</localTime>
              <timeZone>CET-1CEST,M3.5.0/2,M10.5.0/3</timeZone>
            </Time>
            """);
        var resolver = CreateResolver(handler);

        var clock = await resolver.ResolveAsync(CreateNvr(), CancellationToken.None);

        Assert.Equal("detected", clock.Source);
        Assert.Equal("CET-1CEST,M3.5.0/2,M10.5.0/3", clock.TimeZone.Id);
        Assert.Equal(
            TimeSpan.FromHours(2),
            clock.TimeZone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0)));
        Assert.Null(clock.Error);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Caches_the_detected_clock()
    {
        var handler = new StubHandler(
            """
            <Time>
              <timeZone>CET-1CEST,M3.5.0/2,M10.5.0/3</timeZone>
            </Time>
            """);
        var resolver = CreateResolver(handler);
        var nvr = CreateNvr();

        await resolver.ResolveAsync(nvr, CancellationToken.None);
        await resolver.ResolveAsync(nvr, CancellationToken.None);

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task An_explicit_override_wins_without_calling_the_nvr()
    {
        var handler = new StubHandler("<Time><timeZone>CET-1CEST,M3.5.0/2,M10.5.0/3</timeZone></Time>");
        var resolver = CreateResolver(handler);

        var clock = await resolver.ResolveAsync(
            CreateNvr(timeZone: "UTC"),
            CancellationToken.None);

        Assert.Equal("override", clock.Source);
        Assert.Equal(TimeSpan.Zero, clock.TimeZone.GetUtcOffset(new DateTime(2026, 7, 15)));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Falls_back_to_the_current_offset_when_the_zone_is_unparseable()
    {
        var handler = new StubHandler(
            """
            <Time>
              <localTime>2026-11-22T18:00:00+09:00</localTime>
              <timeZone>not a posix zone</timeZone>
            </Time>
            """);
        var resolver = CreateResolver(handler);

        var clock = await resolver.ResolveAsync(CreateNvr(), CancellationToken.None);

        Assert.Equal("detected-offset", clock.Source);
        Assert.Equal(TimeSpan.FromHours(9), clock.TimeZone.GetUtcOffset(new DateTime(2026, 7, 15)));
        Assert.Null(clock.Error);
    }

    [Fact]
    public async Task Falls_back_when_local_time_has_no_explicit_offset()
    {
        var handler = new StubHandler(
            """
            <Time>
              <localTime>2026-11-22T18:00:00</localTime>
              <timeZone>not a posix zone</timeZone>
            </Time>
            """);
        var resolver = CreateResolver(handler);

        var clock = await resolver.ResolveAsync(CreateNvr(), CancellationToken.None);

        Assert.Equal("fallback", clock.Source);
        Assert.NotNull(clock.Error);
    }

    [Fact]
    public async Task Reports_a_fallback_when_the_nvr_clock_is_unavailable()
    {
        var handler = new StubHandler("<Time></Time>");
        var resolver = CreateResolver(handler);

        var clock = await resolver.ResolveAsync(CreateNvr(), CancellationToken.None);

        Assert.Equal("fallback", clock.Source);
        Assert.Equal(TimeSpan.Zero, clock.TimeZone.GetUtcOffset(new DateTime(2026, 7, 15)));
        Assert.NotNull(clock.Error);
    }

    [Fact]
    public async Task Invalidate_forces_a_new_lookup()
    {
        var handler = new StubHandler(
            """
            <Time>
              <timeZone>CET-1CEST,M3.5.0/2,M10.5.0/3</timeZone>
            </Time>
            """);
        var resolver = CreateResolver(handler);
        var nvr = CreateNvr();

        await resolver.ResolveAsync(nvr, CancellationToken.None);
        resolver.Invalidate(nvr.Id);
        await resolver.ResolveAsync(nvr, CancellationToken.None);

        Assert.Equal(2, handler.Requests);
    }

    private static NvrClockResolver CreateResolver(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var endpoints = new IsapiEndpointResolver(
            http,
            new IsapiOptions(8666, InsecureTls: false),
            NullLogger<IsapiEndpointResolver>.Instance);

        return new NvrClockResolver(
            http,
            endpoints,
            NullLogger<NvrClockResolver>.Instance);
    }

    private static NvrDto CreateNvr(string timeZone = "") =>
        new(
            "nvr-1",
            "House NVR",
            "rtsp://127.0.0.1:554",
            NvrTransports.Rtsp,
            NvrTransports.DefaultSdkPort,
            Now,
            Now,
            timeZone);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            });
        }
    }
}
