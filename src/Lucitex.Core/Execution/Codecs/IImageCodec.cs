using Lucitex.Core.Semantic;

namespace Lucitex.Core.Execution.Codecs;

public interface IImageCodec
{
    string FormatId { get; }

    IReadOnlyList<string> Extensions { get; }

    FormatProbeResult Probe(ReadOnlySpan<byte> header);

    IImageReader OpenReader(Stream stream, DecodeLimits? limits = null);

    IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor);
}
