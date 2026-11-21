namespace Peekaboo.Api.Streaming;

/// <summary>
/// Produces an upstream byte stream for a session that does not use a URL input
/// (for example, an SDK callback). The destination is ffmpeg's standard input.
/// </summary>
public interface IStreamInput
{
    Task RunAsync(Stream destination, CancellationToken cancellationToken);
}

public interface IStreamInputFactory
{
    IStreamInput? Create(StreamSource source);
}
