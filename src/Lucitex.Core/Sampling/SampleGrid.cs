using Lucitex.Core.Spatial;

namespace Lucitex.Core.Sampling;

public readonly record struct SampleGrid
{
    public required Long3 Origin { get; init; }

    public required Int3 Step { get; init; }

    public static SampleGrid Unit => new() { Origin = Long3.Zero, Step = Int3.One };
}
