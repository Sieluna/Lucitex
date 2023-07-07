using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;

namespace Lucitex.Core.Representation;

public enum PlaneLayout
{
    Interleaved,
    Planar,
}

public sealed record SamplePlaneDescriptor
{
    public required IReadOnlyList<ChannelPath> Channels { get; init; }

    public required Extent3L Extent { get; init; }

    public PlaneLayout Layout { get; init; } = PlaneLayout.Interleaved;
}

public sealed record PlainSampleRepresentation : PayloadRepresentation
{
    public required IReadOnlyList<SamplePlaneDescriptor> Planes { get; init; }
}
