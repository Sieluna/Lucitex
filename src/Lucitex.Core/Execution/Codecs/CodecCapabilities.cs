using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;

namespace Lucitex.Core.Execution.Codecs;

public enum SampleByteOrder
{
    LittleEndian,
    BigEndian,
}

public sealed record CodecCapabilities
{
    public required IReadOnlyList<SampleType> SupportedSampleTypes { get; init; }

    // Every codec here stores multi-byte samples in its own format's fixed byte order, and Read/Write
    // hand those bytes through as-is - SampleType carries no endianness of its own. A conversion
    // pipeline reading bytes from one codec's Read() and feeding them to a numeric kernel (or another
    // codec's Write()) must know this to interpret/produce them correctly.
    public SampleByteOrder SampleByteOrder { get; init; } = SampleByteOrder.LittleEndian;

    public IReadOnlyList<EncodedFormatId> SupportedEncodedFormats { get; init; } = [];

    public bool SupportsIndexed { get; init; }

    public bool SupportsDeep { get; init; }

    public bool SupportsMultiplePartsPerAsset { get; init; }

    public bool SupportsArbitraryChannelNames { get; init; }

    public bool SupportsOrientationMetadata { get; init; }

    public int MaxChannelsPerPart { get; init; } = int.MaxValue;

    // Null means any channel count is fine (true for PNG/EXR/HDR, which lay channels out as
    // independent planes/samples). DDS and KTX2 instead pick one fixed-layout pixel format
    // (DXGI/VkFormat) for the whole part, and those tables only define 1, 2, or 4-channel formats -
    // there is no hardware 3-channel 8-bit format - so a plan targeting them has to know that up
    // front rather than finding out when the codec-specific format lookup throws.
    public IReadOnlyList<int>? SupportedChannelCounts { get; init; }

    public bool SupportsSampleType(SampleType sampleType) => SupportedSampleTypes.Contains(sampleType);

    public bool SupportsEncodedFormat(EncodedFormatId format) => SupportedEncodedFormats.Contains(format);
}
