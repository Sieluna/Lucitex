using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Ktx2.Format;

namespace Lucitex.Ktx2;

public sealed class Ktx2Codec : IImageCodec
{
    private static ReadOnlySpan<byte> Magic => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public string FormatId => "ktx2";

    public IReadOnlyList<string> Extensions { get; } = [".ktx2"];

    // Same caveat as DdsCodec.Capabilities: Read/Write operate on already-encoded element bytes
    // for every EncodedElementRepresentation, so SupportedEncodedFormats is what the container can
    // structurally carry, not what a conversion pipeline can produce from decoded samples.
    public CodecCapabilities Capabilities { get; } = new() {
        SupportedSampleTypes = [SampleType.UNorm8, SampleType.UNorm16, SampleType.Float16, SampleType.Float32],
        SupportedEncodedFormats =
        [
            EncodedFormatId.R10G10B10A2, EncodedFormatId.R11G11B10Float, EncodedFormatId.Rgb9E5,
            EncodedFormatId.Bc1, EncodedFormatId.Bc2, EncodedFormatId.Bc3, EncodedFormatId.Bc4, EncodedFormatId.Bc5, EncodedFormatId.Bc6H, EncodedFormatId.Bc6HSigned, EncodedFormatId.Bc7,
        ],
        SupportsIndexed = false,
        SupportsDeep = false,
        SupportsMultiplePartsPerAsset = false,
        SupportsArbitraryChannelNames = false,
        SupportsOrientationMetadata = false,
        MaxChannelsPerPart = 4,
    };

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
            return new Ktx2Reader(stream, limits ?? DecodeLimits.Default);
        }
        catch (Exception exception) when (Ktx2FormatErrors.IsMalformed(exception)) {
            throw Ktx2FormatErrors.Wrap(exception, stream);
        }
    }

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) =>
        new Ktx2Writer(stream, descriptor, Ktx2SupercompressionScheme.None);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor, Ktx2SupercompressionScheme scheme) =>
        new Ktx2Writer(stream, descriptor, scheme);
}
