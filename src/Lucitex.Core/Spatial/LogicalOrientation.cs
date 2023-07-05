namespace Lucitex.Core.Spatial;

public enum Axis
{
    X,
    Y,
    Z,
}

public readonly record struct AxisPermutation(Axis X, Axis Y, Axis Z)
{
    public static AxisPermutation Identity => new(Axis.X, Axis.Y, Axis.Z);

    public static AxisPermutation Transpose => new(Axis.Y, Axis.X, Axis.Z);
}

public readonly record struct AxisSign(int X, int Y, int Z)
{
    public static AxisSign Positive => new(1, 1, 1);
}

public readonly record struct LogicalOrientation(AxisPermutation Permutation, AxisSign Sign)
{
    public static LogicalOrientation Identity =>
        new(AxisPermutation.Identity, AxisSign.Positive);

    public static LogicalOrientation FlipX =>
        new(AxisPermutation.Identity, new AxisSign(-1, 1, 1));

    public static LogicalOrientation FlipY =>
        new(AxisPermutation.Identity, new AxisSign(1, -1, 1));

    public static LogicalOrientation Rotate90 =>
        new(AxisPermutation.Transpose, new AxisSign(-1, 1, 1));

    public static LogicalOrientation Rotate180 =>
        new(AxisPermutation.Identity, new AxisSign(-1, -1, 1));

    public static LogicalOrientation Rotate270 =>
        new(AxisPermutation.Transpose, new AxisSign(1, -1, 1));

    public static LogicalOrientation Transpose =>
        new(AxisPermutation.Transpose, AxisSign.Positive);

    public static LogicalOrientation Transverse =>
        new(AxisPermutation.Transpose, new AxisSign(-1, -1, 1));
}

public enum StorageTraversal
{
    IncreasingY,
    DecreasingY,
    Random,
    Sequential,
    CodecDefined,
}
