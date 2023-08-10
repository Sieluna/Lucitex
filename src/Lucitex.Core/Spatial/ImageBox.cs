namespace Lucitex.Core.Spatial;

public readonly record struct ImageBox
{
    public required long MinX { get; init; }
    public required long MinY { get; init; }
    public required long MaxXExclusive { get; init; }
    public required long MaxYExclusive { get; init; }

    public long Width => checked(MaxXExclusive - MinX);
    public long Height => checked(MaxYExclusive - MinY);

    public bool IsEmpty => MaxXExclusive <= MinX || MaxYExclusive <= MinY;

    public static ImageBox FromExclusive(long minX, long minY, long maxXExclusive, long maxYExclusive)
    {
        if (maxXExclusive < minX || maxYExclusive < minY) {
            throw new ArgumentOutOfRangeException(nameof(maxXExclusive), "Box max must not be less than min.");
        }

        return new ImageBox {
            MinX = minX,
            MinY = minY,
            MaxXExclusive = maxXExclusive,
            MaxYExclusive = maxYExclusive,
        };
    }

    public static ImageBox FromInclusive(long minX, long minY, long maxXInclusive, long maxYInclusive)
        => FromExclusive(minX, minY, checked(maxXInclusive + 1), checked(maxYInclusive + 1));

    public static ImageBox FromOrigin(long width, long height)
        => FromExclusive(0, 0, width, height);
}
