using Lucitex.Core.Sampling;

namespace Lucitex.Core.Representation;

public sealed record PaletteDescriptor
{
    public required int EntryCount { get; init; }

    public required ChannelSchema EntryChannels { get; init; }

    public required SampleType EntrySampleType { get; init; }
}

public sealed record IndexedRepresentation : PayloadRepresentation
{
    public required SampleType IndexType { get; init; }

    public required PaletteDescriptor Palette { get; init; }
}
