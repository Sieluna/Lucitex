using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

public sealed class ExrCodec(ExrCompressionId defaultCompression = ExrCompressionId.Zip, ExrTileDesc? tiles = null) : IImageCodec
{
    private static ReadOnlySpan<byte> Magic => [0x76, 0x2f, 0x31, 0x01];

    public string FormatId => "exr";

    public IReadOnlyList<string> Extensions { get; } = [".exr"];

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length < Magic.Length) {
            return FormatProbeResult.NoMatch(Magic.Length);
        }

        return header[..Magic.Length].SequenceEqual(Magic)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = Magic.Length }
            : FormatProbeResult.NoMatch(Magic.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null)
    {
        try {
            return new ExrReader(stream, limits ?? DecodeLimits.Default);
        }
        catch (Exception exception) when (ExrFormatErrors.IsMalformed(exception)) {
            throw ExrFormatErrors.Wrap(exception, stream);
        }
    }

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new ExrWriter(stream, descriptor, defaultCompression, tiles);
}
