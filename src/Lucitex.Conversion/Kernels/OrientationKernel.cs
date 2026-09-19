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

        if (swapsAxes) {
            ApplyWithAxisSwap(source, width, height, destWidth, from.Sign.X, from.Sign.Y, destination);
        }
        else {
            ApplyWithoutAxisSwap(source, width, height, from.Sign.X, from.Sign.Y, destination);
        }

        return (destWidth, destHeight);
    }

    private static void ApplyWithoutAxisSwap(ReadOnlySpan<float> source, int width, int height, int signX, int signY, Span<float> destination)
    {
        var mirrorsRow = signX != 1;

        for (var sy = 0; sy < height; sy++) {
            var destRow = signY == 1 ? sy : height - 1 - sy;
            var destRowSpan = destination.Slice(destRow * width, width);

            source.Slice(sy * width, width).CopyTo(destRowSpan);
            if (mirrorsRow) {
                destRowSpan.Reverse();
            }
        }
    }

    private static void ApplyWithAxisSwap(ReadOnlySpan<float> source, int width, int height, int destWidth, int signX, int signY, Span<float> destination)
    {
        var lastColumn = width - 1;

        for (var sy = 0; sy < height; sy++) {
            var lx = signX == 1 ? sy : height - 1 - sy;
            var sourceRow = source.Slice(sy * width, width);

            if (signY == 1) {
                for (var sx = 0; sx < width; sx++) {
                    destination[(sx * destWidth) + lx] = sourceRow[sx];
                }
            }
            else {
                for (var sx = 0; sx < width; sx++) {
                    destination[((lastColumn - sx) * destWidth) + lx] = sourceRow[sx];
                }
            }
        }
    }
}
