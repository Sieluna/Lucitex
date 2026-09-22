using System.Runtime.Intrinsics;

namespace Lucitex.Jpeg.Decoding;

internal static class InverseDct
{
    public static readonly float[] Basis = BuildBasis();
    public static readonly Vector256<float>[] BasisRows = BuildBasisRows();

    private static readonly Vector256<float>[] s_BasisTransposedRows = BuildBasisTransposedRows();

    public static void Transform(ReadOnlySpan<int> coefficients, Span<float> output)
    {
        if (Vector256.IsHardwareAccelerated) {
            TransformVectorized(coefficients, output);
            return;
        }

        TransformScalar(coefficients, output);
    }

    internal static void TransformVectorized(ReadOnlySpan<int> coefficients, Span<float> output)
    {
        var basis = Basis;
        var basisT = s_BasisTransposedRows;
        Span<float> partial = stackalloc float[64];

        for (var v = 0; v < 8; v++) {
            var rowOffset = v * 8;
            var hasAc = false;
            for (var u = 1; u < 8; u++) {
                if (coefficients[rowOffset + u] != 0) {
                    hasAc = true;
                    break;
                }
            }

            var dc = coefficients[rowOffset];
            if (!hasAc) {
                var dcRow = dc == 0 ? Vector256<float>.Zero : Vector256.Create((float)dc) * basisT[0];
                dcRow.CopyTo(partial.Slice(rowOffset, 8));
                continue;
            }

            var acc = Vector256<float>.Zero;
            for (var u = 0; u < 8; u++) {
                var coefficient = coefficients[rowOffset + u];
                if (coefficient != 0) {
                    acc += Vector256.Create((float)coefficient) * basisT[u];
                }
            }

            acc.CopyTo(partial.Slice(rowOffset, 8));
        }

        var o0 = Vector256<float>.Zero;
        var o1 = Vector256<float>.Zero;
        var o2 = Vector256<float>.Zero;
        var o3 = Vector256<float>.Zero;
        var o4 = Vector256<float>.Zero;
        var o5 = Vector256<float>.Zero;
        var o6 = Vector256<float>.Zero;
        var o7 = Vector256<float>.Zero;

        for (var v = 0; v < 8; v++) {
            var partialRow = Vector256.Create(partial.Slice(v * 8, 8));
            if (partialRow == Vector256<float>.Zero) {
                continue;
            }

            o0 += partialRow * Vector256.Create(basis[(0 * 8) + v]);
            o1 += partialRow * Vector256.Create(basis[(1 * 8) + v]);
            o2 += partialRow * Vector256.Create(basis[(2 * 8) + v]);
            o3 += partialRow * Vector256.Create(basis[(3 * 8) + v]);
            o4 += partialRow * Vector256.Create(basis[(4 * 8) + v]);
            o5 += partialRow * Vector256.Create(basis[(5 * 8) + v]);
            o6 += partialRow * Vector256.Create(basis[(6 * 8) + v]);
            o7 += partialRow * Vector256.Create(basis[(7 * 8) + v]);
        }

        o0.CopyTo(output.Slice(0, 8));
        o1.CopyTo(output.Slice(8, 8));
        o2.CopyTo(output.Slice(16, 8));
        o3.CopyTo(output.Slice(24, 8));
        o4.CopyTo(output.Slice(32, 8));
        o5.CopyTo(output.Slice(40, 8));
        o6.CopyTo(output.Slice(48, 8));
        o7.CopyTo(output.Slice(56, 8));
    }

    internal static void TransformScalar(ReadOnlySpan<int> coefficients, Span<float> output)
    {
        Span<float> partial = stackalloc float[64];
        var basis = Basis;

        for (var v = 0; v < 8; v++) {
            var rowOffset = v * 8;
            var hasAc = false;
            for (var u = 1; u < 8; u++) {
                if (coefficients[rowOffset + u] != 0) {
                    hasAc = true;
                    break;
                }
            }

            var dc = coefficients[rowOffset];
            if (!hasAc) {
                if (dc == 0) {
                    for (var x = 0; x < 8; x++) {
                        partial[rowOffset + x] = 0f;
                    }
                }
                else {
                    for (var x = 0; x < 8; x++) {
                        partial[rowOffset + x] = basis[(x * 8) + 0] * dc;
                    }
                }

                continue;
            }

            for (var x = 0; x < 8; x++) {
                var sum = 0f;
                var basisRow = x * 8;
                for (var u = 0; u < 8; u++) {
                    var coefficient = coefficients[rowOffset + u];
                    if (coefficient != 0) {
                        sum += basis[basisRow + u] * coefficient;
                    }
                }

                partial[rowOffset + x] = sum;
            }
        }

        for (var x = 0; x < 8; x++) {
            for (var y = 0; y < 8; y++) {
                var sum = 0f;
                var basisRow = y * 8;
                for (var v = 0; v < 8; v++) {
                    var p = partial[(v * 8) + x];
                    if (p != 0f) {
                        sum += basis[basisRow + v] * p;
                    }
                }

                output[(y * 8) + x] = sum;
            }
        }
    }

    private static float[] BuildBasis()
    {
        var basis = new float[64];
        for (var position = 0; position < 8; position++) {
            for (var frequency = 0; frequency < 8; frequency++) {
                var normalization = frequency == 0 ? 1.0 / Math.Sqrt(2) : 1.0;
                basis[(position * 8) + frequency] = (float)(0.5 * normalization * Math.Cos((2 * position + 1) * frequency * Math.PI / 16.0));
            }
        }

        return basis;
    }

    private static Vector256<float>[] BuildBasisRows()
    {
        var basis = Basis;
        var rows = new Vector256<float>[8];
        for (var i = 0; i < 8; i++) {
            rows[i] = Vector256.Create(basis.AsSpan(i * 8, 8));
        }

        return rows;
    }

    private static Vector256<float>[] BuildBasisTransposedRows()
    {
        var basis = Basis;
        var rows = new Vector256<float>[8];
        for (var i = 0; i < 8; i++) {
            var column = new float[8];
            for (var j = 0; j < 8; j++) {
                column[j] = basis[(j * 8) + i];
            }

            rows[i] = Vector256.Create((ReadOnlySpan<float>)column);
        }

        return rows;
    }
}
