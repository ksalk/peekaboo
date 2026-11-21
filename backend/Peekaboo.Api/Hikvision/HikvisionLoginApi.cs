using System.Runtime.InteropServices;
using System.Text;
using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Hikvision;

internal sealed record HikvisionDeviceInfo(
    string SerialNumber,
    int AnalogChannelCount,
    int StartChannel,
    int IpChannelCount,
    int StartDigitalChannel);

internal sealed record HikvisionLoginResult(int UserId, HikvisionDeviceInfo Device);

internal interface IHikvisionLoginApi
{
    HikvisionLoginResult Login(string host, int port);

    void Logout(int userId);
}

internal sealed class HikvisionLoginApi(
    CameraCredentials credentials,
    HikvisionSdkRuntime runtime,
    ILogger<HikvisionLoginApi> logger) : IHikvisionLoginApi
{
    public HikvisionLoginResult Login(string host, int port)
    {
        runtime.EnsureInitialized();

        var credential = credentials.CreateCredential()
            ?? throw new HikvisionException(
                "The Hikvision SDK transport requires CAMERA_USERNAME and CAMERA_PASSWORD.",
                HikvisionFailureKind.Authentication);

        if (port is < 1 or > 65535)
        {
            throw new HikvisionException(
                $"The SDK port {port} is not a valid TCP port.",
                HikvisionFailureKind.Unavailable);
        }

        var login = new NetDvrUserLoginInfo
        {
            sDeviceAddress = new byte[129],
            sUserName = new byte[64],
            sPassword = new byte[64],
            byRes1 = new byte[3],
            byRes3 = new byte[109],
            wPort = (ushort)port,
            bUseAsynLogin = 0,
            byLoginMode = 0,
            byHttps = 0,
            // The V50 playback/search structures carry UTC offsets. Leaving the
            // login flag at zero avoids the SDK applying the device offset twice.
            byUseUTCTime = 0,
        };
        CopyUtf8(host, login.sDeviceAddress);
        CopyUtf8(credential.UserName ?? string.Empty, login.sUserName);
        CopyUtf8(credential.Password ?? string.Empty, login.sPassword);

        var device = new NetDvrDeviceInfoV40
        {
            byRes2 = new byte[233],
        };

        var loginSize = Marshal.SizeOf<NetDvrUserLoginInfo>();
        var loginPointer = Marshal.AllocHGlobal(loginSize);
        var userId = -1;

        try
        {
            Marshal.StructureToPtr(login, loginPointer, fDeleteOld: false);
            userId = HikvisionNative.NET_DVR_Login_V40(loginPointer, ref device);
            if (userId < 0)
            {
                throw HikvisionErrorMapper.FromLastError($"log in to {host}:{port}");
            }

            var info = MapDevice(device);
            logger.LogInformation(
                "Hikvision SDK login to {Host}:{Port} succeeded (serial {Serial}, " +
                "{AnalogChannels} analog channels, {IpChannels} IP channels starting at {StartDigital})",
                host,
                port,
                info.SerialNumber,
                info.AnalogChannelCount,
                info.IpChannelCount,
                info.StartDigitalChannel);

            return new HikvisionLoginResult(userId, info);
        }
        catch
        {
            if (userId >= 0 && HikvisionNative.NET_DVR_Logout(userId) == 0)
            {
                logger.LogWarning(
                    "Hikvision SDK cleanup logout for user handle {UserId} failed: {Error}",
                    userId,
                    HikvisionNative.LastErrorText());
            }

            throw;
        }
        finally
        {
            // StructureToPtr creates a native copy, including the password. Clear
            // both the unmanaged copy and the managed array before releasing them.
            Marshal.Copy(new byte[loginSize], 0, loginPointer, loginSize);
            Marshal.FreeHGlobal(loginPointer);
            Array.Clear(login.sPassword);
        }
    }

    public void Logout(int userId)
    {
        if (HikvisionNative.NET_DVR_Logout(userId) == 0)
        {
            logger.LogWarning(
                "Hikvision SDK logout for user handle {UserId} failed: {Error}",
                userId,
                HikvisionNative.LastErrorText());
        }
    }

    private static HikvisionDeviceInfo MapDevice(NetDvrDeviceInfoV40 device)
    {
        var v30 = device.struDeviceV30;
        var serial = Encoding.ASCII.GetString(v30.sSerialNumber).TrimEnd('\0', ' ');
        var ipChannelCount = v30.byIPChanNum + (v30.byHighDChanNum << 8);

        return new HikvisionDeviceInfo(
            serial,
            v30.byChanNum,
            v30.byStartChan,
            ipChannelCount,
            v30.byStartDChan);
    }

    private static void CopyUtf8(string value, byte[] destination)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Array.Copy(bytes, destination, Math.Min(bytes.Length, destination.Length - 1));
    }
}
