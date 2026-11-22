using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Peekaboo.Api.Auth;
using Peekaboo.Api.Alarms;
using Peekaboo.Api.Cameras;
using Peekaboo.Api.Data;
using Peekaboo.Api.Endpoints;
using Peekaboo.Api.Hikvision;
using Peekaboo.Api.Isapi;
using Peekaboo.Api.Nvrs;
using Peekaboo.Api.Playback;
using Peekaboo.Api.Security;
using Peekaboo.Api.Streaming;

var builder = WebApplication.CreateBuilder(args);

var trustProxy = bool.TryParse(builder.Configuration["PEEKABOO_TRUST_PROXY"], out var parsedTrustProxy)
    && parsedTrustProxy;

if (trustProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        // Only enable PEEKABOO_TRUST_PROXY when the app port is reachable exclusively
        // through the reverse proxy; otherwise clients could spoof these headers.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var databasePath = builder.Configuration["PEEKABOO_DB_PATH"] ?? "data/peekaboo.db";
builder.Services.AddSingleton(new AppDatabase(databasePath));
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<NvrService>();
builder.Services.AddSingleton<CameraService>();
builder.Services.AddSingleton(CameraCredentials.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(HikvisionSdkOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<HikvisionSdkRuntime>();
builder.Services.AddSingleton<IHikvisionLoginApi, HikvisionLoginApi>();
builder.Services.AddSingleton<HikvisionConnectionPool>();
builder.Services.AddSingleton<IHikvisionRecordingSearch>(services => new HikvisionRecordingSearch(
    services.GetRequiredService<HikvisionConnectionPool>(),
    services.GetRequiredService<ILogger<HikvisionRecordingSearch>>()));
builder.Services.AddSingleton(IsapiOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<IsapiHttpClient>();
builder.Services.AddSingleton<IsapiEndpointResolver>();
builder.Services.AddSingleton<NvrClockResolver>();
builder.Services.AddSingleton<INvrClockResolver>(
    services => services.GetRequiredService<NvrClockResolver>());
builder.Services.AddSingleton<IIsapiClient, IsapiClient>();
builder.Services.AddSingleton<IIsapiAlarmLogClient, IsapiAlarmLogClient>();
// Any future cache consumer must set MemoryCacheEntryOptions.Size.
builder.Services.AddMemoryCache(options => options.SizeLimit = 100_000);
builder.Services.AddSingleton<AlarmRangeService>();
builder.Services.AddSingleton(services => new PlaybackService(
    services.GetRequiredService<CameraService>(),
    services.GetRequiredService<IIsapiClient>(),
    services.GetRequiredService<IHikvisionRecordingSearch>()));
builder.Services.AddSingleton(StreamOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<StreamSourceFactory>();
builder.Services.AddSingleton<IStreamInputFactory>(services => new HikvisionStreamInputFactory(
    services.GetRequiredService<HikvisionConnectionPool>(),
    services.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<StreamManager>();

builder.Services.AddAuthentication(AuthConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(AuthConstants.Scheme, configureOptions: null);

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(
        new AuthorizationPolicyBuilder(AuthConstants.Scheme)
            .RequireAuthenticatedUser()
            .Build())
    .AddPolicy(
        AuthConstants.AdminPolicy,
        policy => policy.RequireRole(AuthConstants.AdminRole));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "300";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy(
        AuthConstants.LoginRateLimitPolicy,
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.Services.GetRequiredService<AppDatabase>()
    .Initialize(app.Services.GetRequiredService<ILogger<AppDatabase>>());

app.Services.GetRequiredService<AuthService>()
    .SeedAdmin(builder.Configuration["ADMIN_USERNAME"], builder.Configuration["ADMIN_PASSWORD"]);

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "media-src 'self' blob:; " +
        "connect-src 'self'; " +
        "font-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
        else if (context.Context.Request.Path.StartsWithSegments("/assets"))
        {
            context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});

if (trustProxy)
{
    app.UseForwardedHeaders();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapNvrEndpoints();
app.MapCameraEndpoints();
app.MapPlaybackEndpoints();
app.MapAlarmEndpoints();
app.MapStreamEndpoints();

app.MapFallback((HttpContext context, IWebHostEnvironment environment) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        return Results.NotFound();
    }

    if (environment.WebRootPath is not { Length: > 0 } webRoot)
    {
        return Results.NotFound();
    }

    var indexPath = Path.Combine(webRoot, "index.html");
    if (!File.Exists(indexPath))
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = "no-cache";
    return Results.File(indexPath, "text/html; charset=utf-8");
}).AllowAnonymous();

app.Run();

public partial class Program;
