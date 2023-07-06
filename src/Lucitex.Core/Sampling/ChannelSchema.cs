namespace Lucitex.Core.Sampling;

public sealed record ChannelDescriptor
{
    public required ChannelPath Name { get; init; }

    public ChannelSemantic Semantic { get; init; } = ChannelSemantic.Unknown;

    public required SampleType SampleType { get; init; }

    public required SampleGrid Sampling { get; init; }
}

public sealed record ChannelSchema
{
    public required IReadOnlyList<ChannelDescriptor> Channels { get; init; }
}
