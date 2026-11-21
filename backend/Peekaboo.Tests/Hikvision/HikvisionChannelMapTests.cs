using Peekaboo.Api.Hikvision;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

public sealed class HikvisionChannelMapTests
{
    private static HikvisionDeviceInfo Device(
        int analogChannels,
        int startChannel,
        int startDigitalChannel,
        int ipChannels) =>
        new("serial", analogChannels, startChannel, ipChannels, startDigitalChannel);

    [Theory]
    [InlineData(1, 33)]
    [InlineData(2, 34)]
    [InlineData(8, 40)]
    [InlineData(33, 33)]
    [InlineData(40, 40)]
    public void Pure_ip_nvrs_map_logical_channels_from_the_digital_start(
        int isapiChannel,
        int expected)
    {
        var channel = HikvisionChannelMap.Map(Device(0, 1, 33, 8), isapiChannel);
        Assert.Equal(expected, channel);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(5, 33)]
    [InlineData(6, 34)]
    public void Hybrid_nvrs_keep_analog_channels_and_offset_ip_channels(
        int isapiChannel,
        int expected)
    {
        var channel = HikvisionChannelMap.Map(Device(4, 1, 33, 8), isapiChannel);
        Assert.Equal(expected, channel);
    }

    [Fact]
    public void A_device_without_digital_channels_rejects_out_of_range_channels()
    {
        var exception = Assert.Throws<HikvisionException>(
            () => HikvisionChannelMap.Map(Device(4, 1, 0, 0), 5));

        Assert.Equal(HikvisionFailureKind.Channel, exception.Kind);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(41)]
    public void Channels_beyond_both_numbering_ranges_are_rejected(int isapiChannel)
    {
        var exception = Assert.Throws<HikvisionException>(
            () => HikvisionChannelMap.Map(Device(0, 1, 33, 8), isapiChannel));

        Assert.Equal(HikvisionFailureKind.Channel, exception.Kind);
        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Channel_zero_is_rejected()
    {
        Assert.Throws<HikvisionException>(
            () => HikvisionChannelMap.Map(Device(0, 1, 33, 8), 0));
    }

    [Theory]
    [InlineData("101", 1, 0)]
    [InlineData("102", 1, 1)]
    [InlineData("1601", 16, 0)]
    [InlineData("3301", 33, 0)]
    public void Stream_ids_map_to_channel_and_stream_type(
        string streamId,
        int expectedChannel,
        int expectedType)
    {
        Assert.True(HikvisionChannelMap.TryParseStreamId(
            streamId,
            out var channel,
            out var type));
        Assert.Equal(expectedChannel, channel);
        Assert.Equal(expectedType, type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("103")]
    [InlineData("200")]
    [InlineData("abc")]
    public void Invalid_stream_ids_are_rejected(string streamId)
    {
        Assert.False(HikvisionChannelMap.TryParseStreamId(streamId, out _, out _));
    }
}
