using Lucitex.Core.Spatial;

namespace Lucitex.Core.Sampling;

public readonly record struct SampleGrid
{
    public required Long3 Origin { get; init; }

    public required Int3 Step { get; init; }

    public static SampleGrid Unit => new() { Origin = Long3.Zero, Step = Int3.One };

    public bool IsUnit => Step.X == 1 && Step.Y == 1 && Step.Z == 1;

    public bool IncludesColumn(long x) => Includes(x, Origin.X, Step.X);

    public bool IncludesRow(long y) => Includes(y, Origin.Y, Step.Y);

    public long CountColumns(long minX, long maxXInclusive) => Count(minX, maxXInclusive, Origin.X, Step.X);

    public long CountRows(long minY, long maxYInclusive) => Count(minY, maxYInclusive, Origin.Y, Step.Y);

    public long ClampColumnToSample(long x, long minX) => ClampToSample(x, minX, Origin.X, Step.X);

    public long ClampRowToSample(long y, long minY) => ClampToSample(y, minY, Origin.Y, Step.Y);

    private static bool Includes(long coordinate, long origin, int step) => (coordinate - origin) % step == 0;

    private static long Count(long min, long maxInclusive, long origin, int step)
    {
        if (maxInclusive < min) {
            return 0;
        }

        var first = FloorDivide(min - origin, step);
        var last = FloorDivide(maxInclusive - origin, step);
        return last - first + (first * step + origin < min ? 0 : 1);
    }

    private static long ClampToSample(long coordinate, long min, long origin, int step)
    {
        var offset = ((coordinate - origin) % step + step) % step;
        var sampled = coordinate - offset;
        return sampled < min ? sampled + ((long)step * (((min - sampled) + step - 1) / step)) : sampled;
    }

    private static long FloorDivide(long value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
