using System.Runtime.InteropServices;
using Peekaboo.Api.Hikvision;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

/// <summary>
/// These values were measured by compiling a C probe against the Linux x64
/// HCNetSDK.h. They are the contract between managed structs and the native SDK.
/// </summary>
public sealed class HikvisionStructLayoutTests
{
    [Fact]
    public void Device_info_layouts_match_the_native_header()
    {
        AssertSize<NetDvrDeviceInfoV30>(80);
        AssertSize<NetDvrDeviceInfoV40>(344);
        AssertOffset<NetDvrDeviceInfoV30>(nameof(NetDvrDeviceInfoV30.byChanNum), 52);
        AssertOffset<NetDvrDeviceInfoV30>(nameof(NetDvrDeviceInfoV30.byStartChan), 53);
        AssertOffset<NetDvrDeviceInfoV30>(nameof(NetDvrDeviceInfoV30.wDevType), 62);
        AssertOffset<NetDvrDeviceInfoV30>(nameof(NetDvrDeviceInfoV30.byStartDChan), 66);
        AssertOffset<NetDvrDeviceInfoV30>(nameof(NetDvrDeviceInfoV30.byHighDChanNum), 68);
        AssertOffset<NetDvrDeviceInfoV40>(nameof(NetDvrDeviceInfoV40.struDeviceV30), 0);
        AssertOffset<NetDvrDeviceInfoV40>(nameof(NetDvrDeviceInfoV40.bySupportLock), 80);
        AssertOffset<NetDvrDeviceInfoV40>(nameof(NetDvrDeviceInfoV40.byRes2), 111);
    }

    [Fact]
    public void Login_info_layout_matches_the_native_header()
    {
        AssertSize<NetDvrUserLoginInfo>(416);

        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.sDeviceAddress), 0);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byUseTransport), 129);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.wPort), 130);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.sUserName), 132);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.sPassword), 196);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.cbLoginResult), 264);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.pUser), 272);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.bUseAsynLogin), 280);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byProxyType), 284);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byUseUTCTime), 285);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byLoginMode), 286);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byHttps), 287);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.iProxyID), 288);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byVerifyMode), 292);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.cbLoginResultV40), 296);
        AssertOffset<NetDvrUserLoginInfo>(nameof(NetDvrUserLoginInfo.byRes3), 304);
    }

    [Fact]
    public void Preview_info_layout_matches_the_native_header()
    {
        AssertSize<NetDvrPreviewInfo>(280);

        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.lChannel), 0);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.dwStreamType), 4);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.dwLinkMode), 8);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.hPlayWnd), 12);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.bBlocked), 16);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.byProtoType), 57);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.dwDisplayBufNum), 60);
        AssertOffset<NetDvrPreviewInfo>(nameof(NetDvrPreviewInfo.byRes), 68);
    }

    [Fact]
    public void Playback_layouts_match_the_native_header()
    {
        AssertSize<NetDvrStreamInfo>(72);
        AssertSize<NetDvrTimeV50>(12);
        AssertSize<NetDvrVodParaV50>(384);

        AssertOffset<NetDvrStreamInfo>(nameof(NetDvrStreamInfo.byID), 4);
        AssertOffset<NetDvrStreamInfo>(nameof(NetDvrStreamInfo.dwChannel), 36);
        AssertOffset<NetDvrTimeV50>(nameof(NetDvrTimeV50.byISO8601), 7);
        AssertOffset<NetDvrTimeV50>(nameof(NetDvrTimeV50.cTimeDifferenceH), 10);

        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.dwSize), 0);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.struIDInfo), 4);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.struBeginTime), 76);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.struEndTime), 88);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.hWnd), 100);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.byStreamType), 107);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.byLinkMode), 115);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.sUserName), 120);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.sPassword), 152);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.byHls), 371);
        AssertOffset<NetDvrVodParaV50>(nameof(NetDvrVodParaV50.pSavedFileName), 376);
    }

    [Fact]
    public void Recording_search_layouts_match_the_native_header()
    {
        AssertSize<NetDvrTimeSearchCond>(12);
        AssertSize<NetDvrFileCondV50>(424);
        AssertSize<NetDvrFindDataV50>(572);

        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.struStreamID), 0);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.struStartTime), 72);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.struStopTime), 84);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.byStreamType), 99);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.dwFileType), 100);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.dwVolumeNum), 104);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.szCardNum), 112);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.szWorkingDeviceGUID), 144);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.uSpecialFindInfo), 160);
        AssertOffset<NetDvrFileCondV50>(nameof(NetDvrFileCondV50.dwTimeout), 168);

        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.sFileName), 0);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.struStartTime), 100);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.struStopTime), 112);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.struAddr), 124);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.dwFileSize), 272);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.byStreamType), 279);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.dwFileIndex), 280);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.sCardNum), 284);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.dwTotalLenH), 316);
        AssertOffset<NetDvrFindDataV50>(nameof(NetDvrFindDataV50.byBigFileType), 324);
    }

    [Fact]
    public void Runtime_config_layouts_match_the_native_header()
    {
        AssertSize<NetDvrLocalSdkPath>(384);
        AssertSize<NetDvrLocalStreamCallbackCfg>(256);

        AssertOffset<NetDvrLocalSdkPath>(nameof(NetDvrLocalSdkPath.sPath), 0);
        AssertOffset<NetDvrLocalStreamCallbackCfg>(
            nameof(NetDvrLocalStreamCallbackCfg.byPlayBackEndFlag),
            0);
    }

    private static void AssertSize<T>(int expected) where T : struct
    {
        Assert.Equal(expected, Marshal.SizeOf<T>());
    }

    private static void AssertOffset<T>(string fieldName, int expected) where T : struct
    {
        Assert.Equal(expected, Marshal.OffsetOf<T>(fieldName).ToInt32());
    }
}
