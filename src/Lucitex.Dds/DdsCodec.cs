using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;

namespace Lucitex.Dds;

public sealed class DdsCodec : IImageCodec
{
    private static ReadOnlySpan<byte> Magic => "DDS "u8;

    public string FormatId => "dds";

    public IReadOnlyList<string> Extensions { get; } = [".dds"];

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length < Magic.Length)
        {
            return FormatProbeResult.NoMatch(Magic.Length);
        }

        return header[..Magic.Length].SequenceEqual(Magic)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = Magic.Length }
            : FormatProbeResult.NoMatch(Magic.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null) => new DdsReader(stream, limits ?? DecodeLimits.Default);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new DdsWriter(stream, descriptor);
}
