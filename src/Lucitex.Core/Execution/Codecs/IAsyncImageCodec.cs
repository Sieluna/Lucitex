using Lucitex.Core.Semantic;

namespace Lucitex.Core.Execution.Codecs;

public interface IAsyncImageCodec : IImageCodec
{
    Task<IAsyncImageReader> OpenReaderAsync(Stream stream, DecodeLimits? limits = null,
        CancellationToken cancellationToken = default);

    IAsyncImageWriter CreateAsyncWriter(Stream stream, ImageAssetDescriptor descriptor);
}
