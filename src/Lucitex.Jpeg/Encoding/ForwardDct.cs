using System.Runtime.Intrinsics;
using Lucitex.Jpeg.Decoding;

namespace Lucitex.Jpeg.Encoding;

internal static class ForwardDct
{
    public static void Transform(ReadOnlySpan<float> samples, Span<float> coefficients)
    {
        if (Vector256.IsHardwareAccelerated) {
            TransformVectorized(samples, coefficients);
            return;
        }

        TransformScalar(samples, coefficients);
    }

    internal static void TransformVectorized(ReadOnlySpan<float> samples, Span<float> coefficients)
    {
        var basisRows = InverseDct.BasisRows;
        var basis = InverseDct.Basis;
        Span<float> partial = stackalloc float[64];

        for (var y = 0; y < 8; y++) {
            var rowOffset = y * 8;
            var acc = Vector256<float>.Zero;
            for (var x = 0; x < 8; x++) {
                acc += Vector256.Create(samples[rowOffset + x]) * basisRows[x];
            }

            acc.CopyTo(partial.Slice(rowOffset, 8));
        }

        var c0 = Vector256<float>.Zero;
        var c1 = Vector256<float>.Zero;
        var c2 = Vector256<float>.Zero;
        var c3 = Vector256<float>.Zero;
        var c4 = Vector256<float>.Zero;
        var c5 = Vector256<float>.Zero;
        var c6 = Vector256<float>.Zero;
        var c7 = Vector256<float>.Zero;

        for (var y = 0; y < 8; y++) {
            var partialRow = Vector256.Create(partial.Slice(y * 8, 8));
            var basisRowOffset = y * 8;
            c0 += partialRow * Vector256.Create(basis[basisRowOffset]);
            c1 += partialRow * Vector256.Create(basis[basisRowOffset + 1]);
            c2 += partialRow * Vector256.Create(basis[basisRowOffset + 2]);
            c3 += partialRow * Vector256.Create(basis[basisRowOffset + 3]);
            c4 += partialRow * Vector256.Create(basis[basisRowOffset + 4]);
            c5 += partialRow * Vector256.Create(basis[basisRowOffset + 5]);
            c6 += partialRow * Vector256.Create(basis[basisRowOffset + 6]);
            c7 += partialRow * Vector256.Create(basis[basisRowOffset + 7]);
        }

        c0.CopyTo(coefficients.Slice(0, 8));
        c1.CopyTo(coefficients.Slice(8, 8));
        c2.CopyTo(coefficients.Slice(16, 8));
        c3.CopyTo(coefficients.Slice(24, 8));
        c4.CopyTo(coefficients.Slice(32, 8));
        c5.CopyTo(coefficients.Slice(40, 8));
        c6.CopyTo(coefficients.Slice(48, 8));
        c7.CopyTo(coefficients.Slice(56, 8));
    }

    internal static void TransformScalar(ReadOnlySpan<float> samples, Span<float> coefficients)
    {
        Span<float> partial = stackalloc float[64];
        var basis = InverseDct.Basis;

        for (var y = 0; y < 8; y++) {
            var rowOffset = y * 8;
            for (var u = 0; u < 8; u++) {
                var sum = 0f;
                for (var x = 0; x < 8; x++) {
                    sum += samples[rowOffset + x] * basis[(x * 8) + u];
                }

                partial[rowOffset + u] = sum;
            }
        }

        for (var u = 0; u < 8; u++) {
            for (var v = 0; v < 8; v++) {
                var sum = 0f;
                for (var y = 0; y < 8; y++) {
                    sum += partial[(y * 8) + u] * basis[(y * 8) + v];
                }

                coefficients[(v * 8) + u] = sum;
            }
        }
    }
}
