using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Core.Execution;

public readonly record struct WorkRegion
{
    public required SubresourceId Subresource { get; init; }

    public required ImageBox Region { get; init; }
}
