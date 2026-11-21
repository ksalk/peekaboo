using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Peekaboo.Api.Hikvision;

/// <summary>
/// Owns the process-global HCNetSDK lifecycle. The SDK may only be initialized
/// once and must be cleaned up after every login and stream handle is gone.
/// </summary>
internal sealed class HikvisionSdkRuntime(
    HikvisionSdkOptions options,
    ILogger<HikvisionSdkRuntime> logger) : IDisposable
{
    private static readonly object ResolverGate = new();
    private static string? s_sdkPath;
    private static bool s_resolverInstalled;

    private readonly object _gate = new();
    private bool _initialized;
    private bool _disposed;

    public void EnsureInitialized()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_initialized)
            {
                return;
            }

            var sdkPath = ResolveSdkPath();
            ValidateSdkLayout(sdkPath);
            WarnIfLibraryPathMissing(sdkPath);
            InstallResolver(sdkPath);

            var initConfig = new NetDvrLocalSdkPath
            {
                sPath = new byte[256],
                byRes = new byte[128],
            };
            CopyUtf8(sdkPath, initConfig.sPath);

            if (HikvisionNative.NET_DVR_SetSDKInitCfg(
                    HikvisionConstants.NetSdkInitCfgSdkPath,
                    ref initConfig) == 0)
            {
                throw HikvisionErrorMapper.FromLastError("initialize the SDK path");
            }

            ConfigureBundledOpenSsl(sdkPath);

            if (HikvisionNative.NET_DVR_Init() == 0)
            {
                throw HikvisionErrorMapper.FromLastError("initialize the SDK");
            }

            _initialized = true;

            try
            {
                ConfigureStreamCallbacks();
                ConfigureNetwork();
                ConfigureLogging();

                var version = HikvisionNative.NET_DVR_GetSDKBuildVersion();
                logger.LogInformation(
                    "Hikvision SDK initialized (version {Version}) from {SdkPath}",
                    $"{version >> 24}.{(version >> 16) & 0xff}.{(version >> 8) & 0xff}.{version & 0xff}",
                    sdkPath);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_initialized)
            {
                Cleanup();
            }
        }
    }

    private void ConfigureBundledOpenSsl(string sdkPath)
    {
        // Container images keep the SDK's OpenSSL copies out of LD_LIBRARY_PATH so
        // they cannot shadow the distro TLS stack for the rest of the process.
        var openssl = Path.Combine(sdkPath, "private");
        var crypto = Path.Combine(openssl, "libcrypto.so.3");
        var ssl = Path.Combine(openssl, "libssl.so.3");

        if (!File.Exists(crypto) || !File.Exists(ssl))
        {
            return;
        }

        if (HikvisionNative.NET_DVR_SetSDKInitCfg(
                HikvisionConstants.NetSdkInitCfgLibcryptoPath,
                crypto) == 0)
        {
            logger.LogWarning("Could not configure the bundled Hikvision libcrypto path.");
        }

        if (HikvisionNative.NET_DVR_SetSDKInitCfg(
                HikvisionConstants.NetSdkInitCfgLibsslPath,
                ssl) == 0)
        {
            logger.LogWarning("Could not configure the bundled Hikvision libssl path.");
        }
    }

    private void ConfigureStreamCallbacks()
    {
        var config = new NetDvrLocalStreamCallbackCfg
        {
            byPlayBackEndFlag = 1,
            byPlayBackNewPos = 0,
            byRes = new byte[254],
        };

        if (HikvisionNative.NET_DVR_SetSDKLocalCfg(
                HikvisionConstants.NetSdkLocalCfgTypeStreamCallback,
                ref config) == 0)
        {
            throw HikvisionErrorMapper.FromLastError("enable playback end callbacks");
        }
    }

    private void ConfigureNetwork()
    {
        if (HikvisionNative.NET_DVR_SetConnectTime(options.ConnectTimeoutMs, 3) == 0)
        {
            logger.LogWarning("Could not configure the Hikvision SDK connect timeout.");
        }

        if (HikvisionNative.NET_DVR_SetReconnect(options.ReconnectIntervalMs, 1) == 0)
        {
            logger.LogWarning("Could not configure the Hikvision SDK reconnect interval.");
        }
    }

    private void ConfigureLogging()
    {
        if (options.LogLevel == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            logger.LogWarning(
                "PEEKABOO_HIK_SDK_LOG_LEVEL is set but PEEKABOO_HIK_SDK_LOG_DIR is empty; " +
                "SDK file logging stays disabled.");
            return;
        }

        try
        {
            Directory.CreateDirectory(options.LogDirectory);
            if (HikvisionNative.NET_DVR_SetLogToFile(
                    options.LogLevel,
                    options.LogDirectory,
                    autoDelete: 1) == 0)
            {
                logger.LogWarning("Could not enable Hikvision SDK file logging.");
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Could not create the Hikvision SDK log directory {LogDirectory}.",
                options.LogDirectory);
        }
    }

    private string ResolveSdkPath()
    {
        if (!options.IsConfigured)
        {
            throw new HikvisionException(
                "The Hikvision SDK is not configured. Download the Linux SDK, copy its lib " +
                "directory to vendor/hikvision, and set PEEKABOO_HIK_SDK_PATH.",
                HikvisionFailureKind.Unavailable);
        }

        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new HikvisionException(
                "The Hikvision SDK transport is only available on Linux x64.",
                HikvisionFailureKind.Unavailable);
        }

        return Path.GetFullPath(options.SdkPath!);
    }

    private static void ValidateSdkLayout(string sdkPath)
    {
        if (!Directory.Exists(sdkPath))
        {
            throw new HikvisionException(
                $"The Hikvision SDK directory was not found at {sdkPath}.",
                HikvisionFailureKind.Unavailable);
        }

        foreach (var required in new[] { "libhcnetsdk.so", "libHCCore.so", "libhpr.so" })
        {
            if (!File.Exists(Path.Combine(sdkPath, required)))
            {
                throw new HikvisionException(
                    $"The Hikvision SDK at {sdkPath} is missing {required}.",
                    HikvisionFailureKind.Unavailable);
            }
        }

        if (!Directory.Exists(Path.Combine(sdkPath, "HCNetSDKCom")))
        {
            throw new HikvisionException(
                $"The Hikvision SDK at {sdkPath} is missing the HCNetSDKCom directory.",
                HikvisionFailureKind.Unavailable);
        }
    }

    private void WarnIfLibraryPathMissing(string sdkPath)
    {
        var paths = (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.GetFullPath(path))
            .ToHashSet(StringComparer.Ordinal);

        if (!paths.Contains(sdkPath) ||
            !paths.Contains(Path.Combine(sdkPath, "HCNetSDKCom")))
        {
            logger.LogWarning(
                "LD_LIBRARY_PATH does not include {SdkPath} and {ComponentsPath}. " +
                "The Hikvision SDK components may fail to load; use scripts/dev.sh or " +
                "export LD_LIBRARY_PATH before starting Peekaboo.",
                sdkPath,
                Path.Combine(sdkPath, "HCNetSDKCom"));
        }
    }

    private static void InstallResolver(string sdkPath)
    {
        lock (ResolverGate)
        {
            s_sdkPath = sdkPath;

            if (s_resolverInstalled)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(HikvisionNative).Assembly, ResolveLibrary);
            s_resolverInstalled = true;
        }
    }

    private static IntPtr ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (libraryName is "hcnetsdk" or "libhcnetsdk.so")
        {
            var path = Path.Combine(s_sdkPath!, "libhcnetsdk.so");
            if (NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private void Cleanup()
    {
        try
        {
            if (HikvisionNative.NET_DVR_Cleanup() == 0)
            {
                logger.LogWarning("Hikvision SDK cleanup returned an error.");
            }
        }
        finally
        {
            _initialized = false;
        }
    }

    private static void CopyUtf8(string value, byte[] destination)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Array.Copy(bytes, destination, Math.Min(bytes.Length, destination.Length - 1));
    }
}
