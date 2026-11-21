using System.Runtime.InteropServices;

namespace Peekaboo.Api.Hikvision;

/// <summary>
/// Raw P/Invoke surface for libhcnetsdk.so. All calls use the C calling convention;
/// on Linux x64 stdcall is the same ABI as C. Callers must initialize the library
/// through <c>HikvisionSdkRuntime</c> before invoking anything else.
/// </summary>
internal static unsafe class HikvisionNative
{
    private const string Library = "hcnetsdk";

    public const int MaxPath = 256;

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_SetSDKInitCfg(
        int enumType,
        ref NetDvrLocalSdkPath lpInBuff);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int NET_DVR_SetSDKInitCfg(
        int enumType,
        string lpInBuff);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_SetSDKLocalCfg(
        int enumType,
        ref NetDvrLocalStreamCallbackCfg lpInBuff);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_Init();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_Cleanup();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint NET_DVR_GetLastError();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr NET_DVR_GetErrorMsg(IntPtr errorNo);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint NET_DVR_GetSDKBuildVersion();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_SetConnectTime(uint waitTime, uint tryTimes);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_SetReconnect(uint interval, int enableReconnect);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int NET_DVR_SetLogToFile(
        uint logLevel,
        string? logDirectory,
        int autoDelete);

    // The login info is passed as unmanaged memory so the password copy can be
    // zeroed after the call. Do not change this to ref NetDvrUserLoginInfo.
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_Login_V40(
        IntPtr loginInfo,
        ref NetDvrDeviceInfoV40 deviceInfo);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_Logout(int userId);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_RealPlay_V40(
        int userId,
        ref NetDvrPreviewInfo previewInfo,
        delegate* unmanaged[Cdecl]<int, uint, byte*, uint, nint, void> callback,
        nint user);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_SetRealDataCallBackEx(
        int realHandle,
        delegate* unmanaged[Cdecl]<int, uint, byte*, uint, nint, void> callback,
        nint user);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_StopRealPlay(int realHandle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_PlayBackByTime_V50(
        int userId,
        ref NetDvrVodParaV50 vodPara);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_SetPlayDataCallBack_V40(
        int playHandle,
        delegate* unmanaged[Cdecl]<int, uint, byte*, uint, nint, void> callback,
        nint user);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_PlayBackControl_V40(
        int playHandle,
        uint controlCode,
        IntPtr inBuffer,
        uint inLength,
        IntPtr outBuffer,
        IntPtr outLength);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_StopPlayBack(int playHandle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_GetPlayBackPos(int playHandle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_FindFile_V50(
        int userId,
        ref NetDvrFileCondV50 findCondition);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_FindNextFile_V50(
        int findHandle,
        ref NetDvrFindDataV50 findData);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int NET_DVR_FindClose_V30(int findHandle);

    public static string LastErrorText()
    {
        var code = NET_DVR_GetLastError();
        var message = NET_DVR_GetErrorMsg(IntPtr.Zero);
        var text = message == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(message);
        return string.IsNullOrWhiteSpace(text)
            ? $"Hikvision SDK error {code}."
            : $"Hikvision SDK error {code}: {text}";
    }
}
