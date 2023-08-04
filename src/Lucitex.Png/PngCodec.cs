using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;

namespace Lucitex.Png;

public sealed class PngCodec : IImageCodec
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public string FormatId => "png";

    public IReadOnlyList<string> Extensions { get; } = [".png"];

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length < Signature.Length)
        {
            return FormatProbeResult.NoMatch(Signature.Length);
        }

        return header[..Signature.Length].SequenceEqual(Signature)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = Signature.Length }
            : FormatProbeResult.NoMatch(Signature.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null)
    {
        try
        {
            return new PngReader(stream, limits ?? DecodeLimits.Default);
        }
        catch (Exception exception) when (PngFormatErrors.IsMalformed(exception))
        {
            throw PngFormatErrors.Wrap(exception, stream);
        }
    }

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new PngWriter(stream, descriptor);
}
