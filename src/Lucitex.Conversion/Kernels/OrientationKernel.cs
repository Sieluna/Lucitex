using Lucitex.Core.Spatial;

namespace Lucitex.Conversion.Kernels;

// Physically reorients a channel already decoded into the canonical float32 pipeline from an arbitrary
// LogicalOrientation into Identity - needed whenever the target format can't store orientation as
// metadata (CodecCapabilities.SupportsOrientationMetadata is false for all four codecs today, per
// Plane.md §11's split between logical orientation and physical storage order).
//
// This is a coordinate permutation/gather, not a per-lane numeric operation, so it doesn't get the
// same portable-SIMD treatment as SampleTypeConversionKernel or AlphaKernel: System.Numerics.Vector has
// no portable cross-lane shuffle or gather, so reordering pixels is plain scalar/row-copy work. FlipY
// in particular is just reversed row order (a Span.CopyTo per row, already about as fast as this gets);
// the others are genuine scatter and stay scalar.
internal static class OrientationKernel
{
    public static (int Width, int Height) ApplyToIdentity(ReadOnlySpan<float> source, int width, int height, LogicalOrientation from, Span<float> destination)
    {
        if (from.Permutation.Z != Axis.Z || from.Sign.Z != 1) {
            throw new NotSupportedException("OrientationKernel only supports 2D orientations (identity Z axis).");
        }

        var swapsAxes = from.Permutation.X == Axis.Y;
        var destWidth = swapsAxes ? height : width;
        var destHeight = swapsAxes ? width : height;

        for (var sy = 0; sy < height; sy++) {
            for (var sx = 0; sx < width; sx++) {
                var lx = LogicalCoordinate(from.Permutation.X, from.Sign.X, sx, sy, width, height);
                var ly = LogicalCoordinate(from.Permutation.Y, from.Sign.Y, sx, sy, width, height);
                destination[(ly * destWidth) + lx] = source[(sy * width) + sx];
            }
        }

        return (destWidth, destHeight);
    }

    private static int LogicalCoordinate(Axis permutationAxis, int sign, int sx, int sy, int width, int height) => permutationAxis switch {
        Axis.X => sign == 1 ? sx : width - 1 - sx,
        Axis.Y => sign == 1 ? sy : height - 1 - sy,
        _ => throw new NotSupportedException("OrientationKernel only supports 2D orientations (identity Z axis)."),
    };
}
