using Lucitex.Core.Spatial;

namespace Lucitex.Core.Topology;

public sealed record ResolutionLevel
{
    public required LevelKey Key { get; init; }

    public required Extent3L Extent { get; init; }

    public SupercompressionDescriptor? Supercompression { get; init; }
}

public sealed record ResourceTopology
{
    public int SpatialDimensions { get; init; } = 2;

    public required Extent3L BaseExtent { get; init; }

    public int ArrayElementCount { get; init; } = 1;

    public int FaceCount { get; init; } = 1;

    public required IReadOnlyList<ResolutionLevel> Levels { get; init; }
}
