using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;

namespace Lucitex.Jpeg;

public sealed class JpegCodec : IImageCodec
{
    private static ReadOnlySpan<byte> Signature => [0xFF, 0xD8, 0xFF];

    public string FormatId => "jpeg";

    public IReadOnlyList<string> Extensions { get; } = [".jpg", ".jpeg"];

    public CodecCapabilities Capabilities { get; } = new() {
        SupportedSampleTypes = [SampleType.UNorm8],
        SampleByteOrder = SampleByteOrder.BigEndian,
        SupportsIndexed = false,
        SupportsDeep = false,
        SupportsMultiplePartsPerAsset = false,
        SupportsArbitraryChannelNames = false,
        SupportsOrientationMetadata = false,
        MaxChannelsPerPart = 3,
        SupportedChannelCounts = [1, 3],
    };

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length < Signature.Length) {
            return FormatProbeResult.NoMatch(Signature.Length);
        }

        return header[..Signature.Length].SequenceEqual(Signature)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = Signature.Length }
            : FormatProbeResult.NoMatch(Signature.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null) => OpenReader(stream, new JpegDecoderOptions(), limits);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new JpegWriter(stream, descriptor);

    public IImageReader OpenReader(Stream stream, JpegDecoderOptions options, DecodeLimits? limits = null)
    {
        try {
            return new JpegReader(stream, limits ?? DecodeLimits.Default, options);
        }
        catch (Exception exception) when (JpegFormatErrors.IsMalformed(exception)) {
            throw JpegFormatErrors.Wrap(exception, stream);
        }
    }

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor, JpegEncoderOptions options) => new JpegWriter(stream, descriptor, options);
}
