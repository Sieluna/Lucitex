using Lucitex.Core.Semantic;

namespace Lucitex.Core.Execution.Codecs;

public interface IImageReader
{
    public ImageAssetDescriptor Describe();

    public int Read(WorkRegion region, Span<byte> destination);
}
