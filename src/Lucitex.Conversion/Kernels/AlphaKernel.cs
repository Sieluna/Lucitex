using System.Numerics;

namespace Lucitex.Conversion.Kernels;

// Premultiplies/unpremultiplies a color channel by an alpha channel, both already decoded into the
// canonical float32 pipeline (see SampleTypeConversionKernel). Same structural rule as every kernel in
// this namespace: a scalar reference loop plus a Vector<T>-batched loop, validated against each other
// in AlphaKernelTests.
internal static class AlphaKernel
{
    public static void Premultiply(Span<float> color, ReadOnlySpan<float> alpha)
    {
        var i = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            for (; i + lanes <= color.Length; i += lanes) {
                var c = new Vector<float>(color.Slice(i, lanes));
                var a = new Vector<float>(alpha.Slice(i, lanes));
                (c * a).CopyTo(color.Slice(i, lanes));
            }
        }

        for (; i < color.Length; i++) {
            color[i] *= alpha[i];
        }
    }

    public static void Unpremultiply(Span<float> color, ReadOnlySpan<float> alpha)
    {
        var i = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            var zero = Vector<float>.Zero;
            for (; i + lanes <= color.Length; i += lanes) {
                var c = new Vector<float>(color.Slice(i, lanes));
                var a = new Vector<float>(alpha.Slice(i, lanes));
                var isZero = Vector.Equals(a, zero);
                var divided = c / Vector.ConditionalSelect(isZero, Vector<float>.One, a);
                Vector.ConditionalSelect(isZero, zero, divided).CopyTo(color.Slice(i, lanes));
            }
        }

        for (; i < color.Length; i++) {
            color[i] = alpha[i] == 0f ? 0f : color[i] / alpha[i];
        }
    }
}
