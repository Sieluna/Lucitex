using Lucitex.Core.Semantic;

namespace Lucitex.Core.Execution.Codecs;

public interface IImageReader
{
    ImageAssetDescriptor Describe();

    int Read(WorkRegion region, Span<byte> destination);
}
