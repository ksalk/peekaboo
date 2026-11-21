namespace Peekaboo.Api.Hikvision;

internal enum HikvisionFailureKind
{
    Unavailable,
    Authentication,
    Permission,
    Channel,
    Busy,
    Timeout,
    Device,
}

internal sealed class HikvisionException : Exception
{
    public HikvisionException(string message, HikvisionFailureKind kind, uint errorCode = 0)
        : base(message)
    {
        Kind = kind;
        ErrorCode = errorCode;
    }

    public HikvisionFailureKind Kind { get; }

    public uint ErrorCode { get; }
}

internal static class HikvisionErrorMapper
{
    public static HikvisionException FromLastError(string operation) =>
        FromCode(operation, HikvisionNative.NET_DVR_GetLastError());

    public static HikvisionException FromCode(string operation, uint errorCode)
    {
        var (kind, message) = errorCode switch
        {
            HikvisionConstants.NetDvrNoError => (
                HikvisionFailureKind.Device,
                "The Hikvision SDK reported an unknown error."),
            HikvisionConstants.NetDvrPasswordError => (
                HikvisionFailureKind.Authentication,
                "The NVR rejected the SDK username or password."),
            HikvisionConstants.NetDvrNoEnoughPri or HikvisionConstants.NetDvrOperNoPermit => (
                HikvisionFailureKind.Permission,
                "The NVR user does not have permission for this SDK operation."),
            HikvisionConstants.NetDvrNoInit => (
                HikvisionFailureKind.Unavailable,
                "The Hikvision SDK is not initialized."),
            HikvisionConstants.NetDvrChannelError => (
                HikvisionFailureKind.Channel,
                "The NVR rejected the SDK channel number."),
            HikvisionConstants.NetDvrOverMaxLink => (
                HikvisionFailureKind.Busy,
                "The NVR reached its maximum number of SDK client connections."),
            HikvisionConstants.NetDvrNetworkFailConnect => (
                HikvisionFailureKind.Unavailable,
                "Could not connect to the NVR SDK port."),
            HikvisionConstants.NetDvrNetworkRecvTimeout => (
                HikvisionFailureKind.Timeout,
                "The NVR did not answer the SDK request in time."),
            HikvisionConstants.NetDvrNoSupport => (
                HikvisionFailureKind.Device,
                "The NVR does not support this SDK operation."),
            _ => (HikvisionFailureKind.Device, $"The Hikvision SDK reported error {errorCode}."),
        };

        return new HikvisionException($"{message} ({operation})", kind, errorCode);
    }
}
