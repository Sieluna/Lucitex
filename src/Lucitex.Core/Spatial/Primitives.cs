namespace Lucitex.Core.Spatial;

public readonly record struct Int3(int X, int Y, int Z)
{
    public static Int3 One => new(1, 1, 1);
}

public readonly record struct Long3(long X, long Y, long Z)
{
    public static Long3 Zero => new(0, 0, 0);
}

public readonly record struct Extent3I(int Width, int Height, int Depth)
{
    public long TexelCount => checked((long)Width * Height * Depth);
}

public readonly record struct Extent3L(long Width, long Height, long Depth)
{
    public long TexelCount => checked(Width * Height * Depth);
}
