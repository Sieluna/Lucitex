namespace Lucitex.Jpeg.Decoding;

internal static class InverseDct
{
    public static readonly float[] Basis = BuildBasis();

    public static void Transform(ReadOnlySpan<int> coefficients, Span<float> output)
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
}
