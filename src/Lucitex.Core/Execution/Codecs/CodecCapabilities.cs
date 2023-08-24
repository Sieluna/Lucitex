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

    public bool SupportsSampleType(SampleType sampleType) => SupportedSampleTypes.Contains(sampleType);

    public bool SupportsEncodedFormat(EncodedFormatId format) => SupportedEncodedFormats.Contains(format);
}
