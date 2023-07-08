using Lucitex.Core.Color;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Core.Semantic;

public sealed record ImagePartDescriptor
{
    public string? Name { get; init; }

    public required SpatialDomain Spatial { get; init; }

    public required ResourceTopology Topology { get; init; }

    public required ChannelSchema Channels { get; init; }

    public required PayloadRepresentation Representation { get; init; }

    public ColorEncoding? Color { get; init; }

    public AlphaDescriptor? Alpha { get; init; }

    public MetadataCollection Metadata { get; init; } = MetadataCollection.Empty;
}
