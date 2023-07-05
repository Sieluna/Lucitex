namespace Lucitex.Core.Spatial;

public sealed record SpatialDomain
{
    public required ImageBox DataWindow { get; init; }

    public required ImageBox DisplayWindow { get; init; }

    public LogicalOrientation Orientation { get; init; } = LogicalOrientation.Identity;

    public StorageTraversal Traversal { get; init; } = StorageTraversal.CodecDefined;

    public double PixelAspectRatio { get; init; } = 1.0;
}
