using System.Globalization;

namespace Peekaboo.Api.Hikvision;

/// <summary>
/// Translates the logical ISAPI channel in a stored stream ID (101 -> 1) into the
/// channel number expected by NET_DVR_RealPlay_V40. Hikvision NVRs number digital
/// channels from byStartDChan (for example 33), not from the logical ISAPI channel.
/// </summary>
internal static class HikvisionChannelMap
{
    public static int Map(HikvisionDeviceInfo device, int isapiChannel)
    {
        if (isapiChannel < 1)
        {
            throw new HikvisionException(
                $"The ISAPI channel {isapiChannel} is not a positive channel number.",
                HikvisionFailureKind.Channel);
        }

        if (device.IpChannelCount > 0)
        {
            var logicalMaximum = device.AnalogChannelCount + device.IpChannelCount;
            var deviceMaximum = device.StartDigitalChannel > 0
                ? device.StartDigitalChannel + device.IpChannelCount - 1
                : 0;
            var isLogicalChannel = isapiChannel <= logicalMaximum;
            var isDeviceChannel = device.StartDigitalChannel > 0 &&
                isapiChannel >= device.StartDigitalChannel &&
                isapiChannel <= deviceMaximum;

            if (!isLogicalChannel && !isDeviceChannel)
            {
                throw new HikvisionException(
                    $"The ISAPI channel {isapiChannel} is outside the NVR's " +
                    $"{logicalMaximum} configured channels.",
                    HikvisionFailureKind.Channel);
            }
        }

        if (device.AnalogChannelCount > 0 && isapiChannel <= device.AnalogChannelCount)
        {
            return isapiChannel;
        }

        if (device.StartDigitalChannel > 0)
        {
            if (isapiChannel >= device.StartDigitalChannel)
            {
                return isapiChannel;
            }

            var mapped = device.StartDigitalChannel +
                (isapiChannel - device.AnalogChannelCount - 1);

            if (mapped >= 1)
            {
                return mapped;
            }
        }

        throw new HikvisionException(
            $"Could not map ISAPI channel {isapiChannel} to an SDK channel " +
            $"(analog channels: {device.AnalogChannelCount}, digital channels start at " +
            $"{device.StartDigitalChannel}).",
            HikvisionFailureKind.Channel);
    }

    public static bool TryParseStreamId(
        string streamId,
        out int isapiChannel,
        out int streamType)
    {
        isapiChannel = 0;
        streamType = 0;

        if (!int.TryParse(
                streamId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }

        var channel = value / 100;
        var kind = value % 100;

        if (channel < 1 || kind is not (1 or 2))
        {
            return false;
        }

        isapiChannel = channel;
        streamType = kind - 1;
        return true;
    }
}
