using Lucitex.Core.Semantic;

namespace Lucitex.Core.Execution.Codecs;

public interface IImageCodec
{
    public string FormatId { get; }

    public IReadOnlyList<string> Extensions { get; }

    public CodecCapabilities Capabilities { get; }

    public FormatProbeResult Probe(ReadOnlySpan<byte> header);

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor);
}
