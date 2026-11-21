namespace Peekaboo.Api.Hikvision;

/// <summary>
/// Constants from the Hikvision Device Network SDK. Values are copied from
/// HCNetSDK.h; do not change them without checking the header.
/// </summary>
internal static class HikvisionConstants
{
    public const int NetSdkInitCfgSdkPath = 2;
    public const int NetSdkInitCfgLibcryptoPath = 3;
    public const int NetSdkInitCfgLibsslPath = 4;
    public const int NetSdkLocalCfgTypeStreamCallback = 16;

    public const int NetDvrPlayStart = 1;
    public const int NetDvrPlayGetPos = 13;
    public const int NetDvrSetTransType = 32;

    public const int NetDvrSysHead = 1;
    public const int NetDvrStreamData = 2;

    // Callback dwDataType value, not a NET_DVR_PlayBackControl_V40 command.
    public const int NetDvrPlaybackAllFileEnd = 12;

    public const int NetDvrFileSuccess = 1000;
    public const int NetDvrFileNoFind = 1001;
    public const int NetDvrIsFinding = 1002;
    public const int NetDvrNoMoreFile = 1003;

    public const int NetDvrNoError = 0;
    public const int NetDvrPasswordError = 1;
    public const int NetDvrNoEnoughPri = 2;
    public const int NetDvrNoInit = 3;
    public const int NetDvrChannelError = 4;
    public const int NetDvrOverMaxLink = 5;
    public const int NetDvrNetworkFailConnect = 7;
    public const int NetDvrNetworkRecvTimeout = 10;
    public const int NetDvrNetworkErrorData = 11;
    public const int NetDvrOperNoPermit = 13;
    public const int NetDvrNoSupport = 23;

    public const int NetDvrStreamTypeMain = 0;
    public const int NetDvrStreamTypeSub = 1;

    public const int NetDvrTransTypePs = 1;
    public const int NetDvrTransTypeTs = 2;
}
