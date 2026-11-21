using System.Runtime.InteropServices;

namespace Peekaboo.Api.Hikvision;

// Layouts mirror the Linux x64 definitions in HCNetSDK.h. Sizes and offsets are
// pinned by HikvisionStructLayoutTests; changing a field without re-checking the
// header will break the native ABI silently.

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrLocalSdkPath
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public byte[] sPath;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    public byte[] byRes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrLocalStreamCallbackCfg
{
    public byte byPlayBackEndFlag;
    public byte byPlayBackNewPos;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 254)]
    public byte[] byRes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrDeviceInfoV30
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
    public byte[] sSerialNumber;

    public byte byAlarmInPortNum;
    public byte byAlarmOutPortNum;
    public byte byDiskNum;
    public byte byDVRType;
    public byte byChanNum;
    public byte byStartChan;
    public byte byAudioChanNum;
    public byte byIPChanNum;
    public byte byZeroChanNum;
    public byte byMainProto;
    public byte bySubProto;
    public byte bySupport;
    public byte bySupport1;
    public byte bySupport2;
    public ushort wDevType;
    public byte bySupport3;
    public byte byMultiStreamProto;
    public byte byStartDChan;
    public byte byStartDTalkChan;
    public byte byHighDChanNum;
    public byte bySupport4;
    public byte byLanguageType;
    public byte byVoiceInChanNum;
    public byte byStartVoiceInChanNo;
    public byte bySupport5;
    public byte bySupport6;
    public byte byMirrorChanNum;
    public ushort wStartMirrorChanNo;
    public byte bySupport7;
    public byte byRes2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrDeviceInfoV40
{
    public NetDvrDeviceInfoV30 struDeviceV30;
    public byte bySupportLock;
    public byte byRetryLoginTime;
    public byte byPasswordLevel;
    public byte byProxyType;
    public uint dwSurplusLockTime;
    public byte byCharEncodeType;
    public byte bySupportDev5;
    public byte bySupport;
    public byte byLoginMode;
    public uint dwOEMCode;
    public int iResidualValidity;
    public byte byResidualValidity;
    public byte bySingleStartDTalkChan;
    public byte bySingleDTalkChanNums;
    public byte byPassWordResetLevel;
    public byte bySupportStreamEncrypt;
    public byte byMarketType;
    public byte byTLSCap;
    public byte byChildManage;
    public byte byPlaybackNewPosCap;
    public byte bySecondaryAuth;
    public byte byHttpsResult;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 233)]
    public byte[] byRes2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrUserLoginInfo
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)]
    public byte[] sDeviceAddress;

    public byte byUseTransport;
    public ushort wPort;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] sUserName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public byte[] sPassword;

    public IntPtr cbLoginResult;
    public IntPtr pUser;
    public int bUseAsynLogin;
    public byte byProxyType;
    public byte byUseUTCTime;
    public byte byLoginMode;
    public byte byHttps;
    public int iProxyID;
    public byte byVerifyMode;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] byRes1;

    public IntPtr cbLoginResultV40;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 109)]
    public byte[] byRes3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrPreviewInfo
{
    public int lChannel;
    public uint dwStreamType;
    public uint dwLinkMode;
    public uint hPlayWnd;
    public uint bBlocked;
    public uint bPassbackRecord;
    public byte byPreviewMode;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] byStreamID;

    public byte byProtoType;
    public byte byRes1;
    public byte byVideoCodingType;
    public uint dwDisplayBufNum;
    public byte byNPQMode;
    public byte byRecvMetaData;
    public byte byDataType;
    public byte byReconnect;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 212)]
    public byte[] byRes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrStreamInfo
{
    public uint dwSize;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] byID;

    public uint dwChannel;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] byRes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrTimeV50
{
    public ushort wYear;
    public byte byMonth;
    public byte byDay;
    public byte byHour;
    public byte byMinute;
    public byte bySecond;
    public byte byISO8601;
    public ushort wMillisecond;
    public sbyte cTimeDifferenceH;
    public sbyte cTimeDifferenceM;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrVodParaV50
{
    public uint dwSize;
    public NetDvrStreamInfo struIDInfo;
    public NetDvrTimeV50 struBeginTime;
    public NetDvrTimeV50 struEndTime;
    public uint hWnd;
    public byte byDrawFrame;
    public byte byVolumeType;
    public byte byVolumeNum;
    public byte byStreamType;
    public uint dwFileIndex;
    public byte byAudioFile;
    public byte byCourseFile;
    public byte byPlayMode;
    public byte byLinkMode;
    public byte byDownload;
    public byte byOptimalStreamType;
    public byte byDisplayBufNum;
    public byte byNPQMode;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] sUserName;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] sPassword;

    public byte byRemoteFile;
    public byte byUseAsyn;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 201)]
    public byte[] byRes2;

    public byte byHls;
    public IntPtr pSavedFileName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrTimeSearchCond
{
    public ushort wYear;
    public byte byMonth;
    public byte byDay;
    public byte byHour;
    public byte byMinute;
    public byte bySecond;
    public byte byLocalOrUTC;
    public ushort wMillisecond;
    public sbyte cTimeDifferenceH;
    public sbyte cTimeDifferenceM;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrFileCondV50
{
    public NetDvrStreamInfo struStreamID;
    public NetDvrTimeSearchCond struStartTime;
    public NetDvrTimeSearchCond struStopTime;
    public byte byFindType;
    public byte byDrawFrame;
    public byte byQuickSearch;
    public byte byStreamType;
    public uint dwFileType;
    public uint dwVolumeNum;
    public byte byIsLocked;
    public byte byNeedCard;
    public byte byOnlyAudioFile;
    public byte bySpecialFindInfoType;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] szCardNum;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] szWorkingDeviceGUID;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] uSpecialFindInfo;

    public uint dwTimeout;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 252)]
    public byte[] byRes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrTimeSearch
{
    public ushort wYear;
    public byte byMonth;
    public byte byDay;
    public byte byHour;
    public byte byMinute;
    public byte bySecond;
    public sbyte cTimeDifferenceH;
    public sbyte cTimeDifferenceM;
    public byte byLocalOrUTC;
    public ushort wMillisecond;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NetDvrFindDataV50
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
    public byte[] sFileName;

    public NetDvrTimeSearch struStartTime;
    public NetDvrTimeSearch struStopTime;

    // NET_DVR_ADDRESS is 148 bytes in the Linux x64 ABI.
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 148)]
    public byte[] struAddr;

    public uint dwFileSize;
    public byte byLocked;
    public byte byFileType;
    public byte byQuickSearch;
    public byte byStreamType;
    public uint dwFileIndex;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] sCardNum;

    public uint dwTotalLenH;
    public uint dwTotalLenL;
    public byte byBigFileType;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 247)]
    public byte[] byRes;
}
