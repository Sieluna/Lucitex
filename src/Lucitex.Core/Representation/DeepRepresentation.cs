using Lucitex.Core.Sampling;

namespace Lucitex.Core.Representation;

public sealed record DeepRepresentation : PayloadRepresentation
{
    public required ChannelSchema SampleChannels { get; init; }

    public required SampleType OffsetType { get; init; }
}
