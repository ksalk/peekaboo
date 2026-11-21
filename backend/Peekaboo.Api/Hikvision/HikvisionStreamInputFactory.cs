using Peekaboo.Api.Streaming;

namespace Peekaboo.Api.Hikvision;

internal sealed class HikvisionStreamInputFactory(
    HikvisionConnectionPool pool,
    ILoggerFactory loggerFactory) : IStreamInputFactory
{
    public IStreamInput? Create(StreamSource source) =>
        source switch
        {
            HikvisionLiveStreamSource live => new HikvisionLiveInput(
                live,
                pool,
                loggerFactory.CreateLogger<HikvisionLiveInput>()),
            _ => null,
        };
}
