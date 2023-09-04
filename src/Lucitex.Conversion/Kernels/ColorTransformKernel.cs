namespace Lucitex.Conversion.Kernels;

// Converts a channel already decoded into the canonical float32 pipeline between linear light and the
// sRGB transfer function (IEC 61966-2-1).
//
// Unlike every other kernel in this namespace, this one is scalar-only for now: the sRGB curve's
// non-linear segment is a real power function (x^(1/2.4) and x^2.4), and System.Numerics.Vector has no
// portable vectorized Pow. A fast bit-trick approximate pow exists in graphics folklore, but shipping
// one without being able to verify its actual error empirically (the way SampleTypeConversionKernel's
// half<->float bit trick was verified against .NET's own Half conversion) would trade a real accuracy
// risk in color-managed output for an unmeasured speed guess - not a trade this kernel makes silently.
// A validated vectorized approximation, gated behind the non-BitExact Determinism levels named in
// Plane.md §46, is left as a follow-up.
internal static class ColorTransformKernel
{
    private const float k_SrgbLinearThreshold = 0.0031308f;
    private const float k_SrgbEncodedThreshold = 0.04045f;

    public static void LinearToSrgb(Span<float> values)
    {
        for (var i = 0; i < values.Length; i++) {
            values[i] = LinearToSrgb(values[i]);
        }
    }

    public static void SrgbToLinear(Span<float> values)
    {
        for (var i = 0; i < values.Length; i++) {
            values[i] = SrgbToLinear(values[i]);
        }
    }

    private static float LinearToSrgb(float linear)
    {
        var sign = linear < 0f ? -1f : 1f;
        var magnitude = MathF.Abs(linear);

        var encoded = magnitude <= k_SrgbLinearThreshold
            ? magnitude * 12.92f
            : (1.055f * MathF.Pow(magnitude, 1f / 2.4f)) - 0.055f;

        return sign * encoded;
    }

    private static float SrgbToLinear(float encoded)
    {
        var sign = encoded < 0f ? -1f : 1f;
        var magnitude = MathF.Abs(encoded);

        var linear = magnitude <= k_SrgbEncodedThreshold
            ? magnitude / 12.92f
            : MathF.Pow((magnitude + 0.055f) / 1.055f, 2.4f);

        return sign * linear;
    }
}
