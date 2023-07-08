using Lucitex.Core.Metadata;

namespace Lucitex.Core.Semantic;

public sealed record ImageAssetDescriptor
{
    public required IReadOnlyList<ImagePartDescriptor> Parts { get; init; }

    public MetadataCollection Metadata { get; init; } = MetadataCollection.Empty;
}
