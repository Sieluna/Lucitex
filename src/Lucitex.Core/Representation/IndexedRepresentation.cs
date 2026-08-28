using Lucitex.Core.Sampling;

namespace Lucitex.Core.Representation;

public sealed record PaletteDescriptor
{
    public required int EntryCount { get; init; }

    public required ChannelSchema EntryChannels { get; init; }

    public required SampleType EntrySampleType { get; init; }

    // Entry values interleaved in EntryChannels order; null if the reader only described the
    // palette's shape without attaching its content.
    public byte[]? RawEntries { get; init; }
}

public sealed record IndexedRepresentation : PayloadRepresentation
{
    public required SampleType IndexType { get; init; }

    public required PaletteDescriptor Palette { get; init; }
}
