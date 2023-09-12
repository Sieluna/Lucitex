using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;

namespace Lucitex.Dds;

public sealed class DdsCodec : IImageCodec
{
    private static ReadOnlySpan<byte> Magic => "DDS "u8;

    public string FormatId => "dds";

    public IReadOnlyList<string> Extensions { get; } = [".dds"];

    // Read/Write always operate on already-encoded element bytes for every EncodedElementRepresentation
    // (packed, shared-exponent, or block-compressed alike) - the container never does sample-level
    // encode/decode itself. SupportedEncodedFormats therefore describes what the DDS container can
    // structurally carry, not what a conversion pipeline can produce from decoded samples; that depends
    // on whether a kernel exists for the given EncodedFormatId (e.g. Compression/Bc1Codec etc.), which
    // is a Lucitex.Conversion concern, not a codec one.
    public CodecCapabilities Capabilities { get; } = new() {
        SupportedSampleTypes = [SampleType.UNorm8, SampleType.UNorm16, SampleType.Float16, SampleType.Float32],
        SupportedEncodedFormats =
        [
            EncodedFormatId.R10G10B10A2, EncodedFormatId.B5G6R5, EncodedFormatId.B5G5R5A1,
            EncodedFormatId.R11G11B10Float, EncodedFormatId.Rgb9E5,
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

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null) => new DdsReader(stream, limits ?? DecodeLimits.Default);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new DdsWriter(stream, descriptor);
}
