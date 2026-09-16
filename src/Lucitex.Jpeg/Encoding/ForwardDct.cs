using Lucitex.Jpeg.Decoding;

namespace Lucitex.Jpeg.Encoding;

internal static class ForwardDct
{
    public static void Transform(ReadOnlySpan<float> samples, Span<float> coefficients)
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
