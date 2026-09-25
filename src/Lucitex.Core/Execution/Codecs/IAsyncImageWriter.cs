namespace Lucitex.Core.Execution.Codecs;

public interface IAsyncImageWriter : IImageWriter, IAsyncDisposable
{
    ValueTask WriteAsync(WorkRegion region, ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    Task FinishAsync(CancellationToken cancellationToken = default);
}
