namespace Lucitex.Core.Execution.Codecs;

public interface IAsyncImageReader : IImageReader, IAsyncDisposable
{
    ValueTask<int> ReadAsync(WorkRegion region, Memory<byte> destination,
        CancellationToken cancellationToken = default);
}
