using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Ktx2.Format;

namespace Lucitex.Ktx2;

public sealed class Ktx2Codec : IImageCodec
{
    private static ReadOnlySpan<byte> Magic => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public string FormatId => "ktx2";

    public IReadOnlyList<string> Extensions { get; } = [".ktx2"];

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length < Magic.Length) {
            return FormatProbeResult.NoMatch(Magic.Length);
        }

        return header[..Magic.Length].SequenceEqual(Magic)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = Magic.Length }
            : FormatProbeResult.NoMatch(Magic.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null) => new Ktx2Reader(stream, limits ?? DecodeLimits.Default);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) =>
        new Ktx2Writer(stream, descriptor, Ktx2SupercompressionScheme.None);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor, Ktx2SupercompressionScheme scheme) =>
        new Ktx2Writer(stream, descriptor, scheme);
}
