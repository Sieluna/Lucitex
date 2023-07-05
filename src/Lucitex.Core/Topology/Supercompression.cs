namespace Lucitex.Core.Topology;

public enum SupercompressionScheme
{
    None,
    BasisLZ,
    Zstandard,
    Zlib,
}

public sealed record SupercompressionDescriptor
{
    public SupercompressionScheme Scheme { get; init; } = SupercompressionScheme.None;

    public long? UncompressedByteLength { get; init; }
}
