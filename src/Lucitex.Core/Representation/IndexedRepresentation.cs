using Lucitex.Core.Sampling;

namespace Lucitex.Core.Representation;

public sealed record PaletteDescriptor
{
    public required int EntryCount { get; init; }

    public required ChannelSchema EntryChannels { get; init; }

    public required SampleType EntrySampleType { get; init; }

    // Entry values, interleaved in EntryChannels order using EntrySampleType's byte width, one entry
    // after another - the same layout a single-pixel-tall interleaved plane would use. Null means the
    // reader that produced this descriptor didn't attach the palette content (only its shape), so a
    // consumer can't expand the indexed representation on its own.
    public byte[]? RawEntries { get; init; }
}

public sealed record IndexedRepresentation : PayloadRepresentation
{
    public required SampleType IndexType { get; init; }

    public required PaletteDescriptor Palette { get; init; }
}
