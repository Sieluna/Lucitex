using Lucitex.Core.Spatial;

namespace Lucitex.Core.Execution.Codecs;

public enum WriteOrder
{
    Increasing,
    Decreasing,
    Arbitrary,
}

public sealed record WriterExecutionContract
{
    public bool RequiresDescriptorUpfront { get; init; } = true;

    public bool RequiresDimensionsUpfront { get; init; } = true;

    public required Extent3I WriteGranularity { get; init; }

    public WriteOrder WriteOrder { get; init; } = WriteOrder.Arbitrary;

    public bool RandomAccess { get; init; }

    public bool RequiresSeekableOutput { get; init; }

    public bool SupportsIncompleteLevels { get; init; }

    public bool SupportsSparseRegions { get; init; }
}
