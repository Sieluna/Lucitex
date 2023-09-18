using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;

namespace Lucitex.Hdr;

public sealed class HdrCodec : IImageCodec
{
    private static ReadOnlySpan<byte> RadianceSignature => "#?RADIANCE"u8;

    private static ReadOnlySpan<byte> RgbeSignature => "#?RGBE"u8;

    public string FormatId => "hdr";

    public IReadOnlyList<string> Extensions { get; } = [".hdr", ".pic"];

    public CodecCapabilities Capabilities { get; } = new() {
        SupportedSampleTypes = [SampleType.Float32],
        SupportedEncodedFormats = [EncodedFormatId.Rgbe],
        SupportsIndexed = false,
        SupportsDeep = false,
        SupportsMultiplePartsPerAsset = false,
        SupportsArbitraryChannelNames = false,
        SupportsOrientationMetadata = true,
        MaxChannelsPerPart = 3,
    };

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length >= RadianceSignature.Length && header[..RadianceSignature.Length].SequenceEqual(RadianceSignature)) {
            return Match("radiance", RadianceSignature.Length);
        }

        if (header.Length >= RgbeSignature.Length && header[..RgbeSignature.Length].SequenceEqual(RgbeSignature)) {
            return Match("rgbe", RgbeSignature.Length);
        }

        return FormatProbeResult.NoMatch(RgbeSignature.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null)
    {
        try {
            return new HdrReader(stream, limits ?? DecodeLimits.Default);
        }
        catch (Exception exception) when (HdrFormatErrors.IsMalformed(exception)) {
            throw HdrFormatErrors.Wrap(exception, stream);
        }
    }

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new HdrWriter(stream, descriptor);

    private static FormatProbeResult Match(string variant, int requiredBytes) => new() {
        Format = "hdr",
        Confidence = ProbeConfidence.Certain,
        RequiredBytes = requiredBytes,
        Variant = variant,
    };
}
