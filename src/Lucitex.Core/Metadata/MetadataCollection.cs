namespace Lucitex.Core.Metadata;

public sealed record MetadataEntry
{
    public required string Namespace { get; init; }

    public required string Name { get; init; }

    public MetadataValue? TypedValue { get; init; }

    public byte[]? RawRepresentation { get; init; }
}

public sealed record MetadataCollection
{
    public IReadOnlyList<MetadataEntry> Entries { get; init; } = [];

    public static MetadataCollection Empty => new();
}
