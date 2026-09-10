using System.Numerics;
using System.Runtime.CompilerServices;

namespace Lucitex.Conversion.Kernels;

internal static class ColorTransformKernel
{
    private const float k_SrgbLinearThreshold = 0.0031308f;
    private const float k_SrgbEncodedThreshold = 0.04045f;

    private const float k_InverseLn2 = 1.44269504088896341f;

    private static readonly PowerExponent s_EncodeExponent = new(1f / 2.4f);
    private static readonly PowerExponent s_DecodeExponent = new(2.4f);
    private const float k_SquareRootOfTwo = 1.41421356237309505f;

    public static void LinearToSrgb(Span<float> values)
    {
        var i = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            for (; i + (4 * lanes) <= values.Length; i += 4 * lanes) {
                var a = EncodeBatch(new Vector<float>(values.Slice(i, lanes)));
                var b = EncodeBatch(new Vector<float>(values.Slice(i + lanes, lanes)));
                var c = EncodeBatch(new Vector<float>(values.Slice(i + (2 * lanes), lanes)));
                var d = EncodeBatch(new Vector<float>(values.Slice(i + (3 * lanes), lanes)));

                a.CopyTo(values.Slice(i, lanes));
                b.CopyTo(values.Slice(i + lanes, lanes));
                c.CopyTo(values.Slice(i + (2 * lanes), lanes));
                d.CopyTo(values.Slice(i + (3 * lanes), lanes));
            }

            for (; i + lanes <= values.Length; i += lanes) {
                EncodeBatch(new Vector<float>(values.Slice(i, lanes))).CopyTo(values.Slice(i, lanes));
            }
        }

        for (; i < values.Length; i++) {
            values[i] = LinearToSrgb(values[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> EncodeBatch(Vector<float> value)
    {
        var threshold = new Vector<float>(k_SrgbLinearThreshold);
        var magnitude = Vector.Abs(value);

        var straight = magnitude * new Vector<float>(12.92f);
        var curved = (Power(Vector.Max(magnitude, threshold), s_EncodeExponent) * new Vector<float>(1.055f)) - new Vector<float>(0.055f);

        return ApplySign(Vector.ConditionalSelect(Vector.LessThanOrEqual(magnitude, threshold), straight, curved), value, magnitude);
    }

    public static void SrgbToLinear(Span<float> values)
    {
        var i = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            for (; i + (4 * lanes) <= values.Length; i += 4 * lanes) {
                var a = DecodeBatch(new Vector<float>(values.Slice(i, lanes)));
                var b = DecodeBatch(new Vector<float>(values.Slice(i + lanes, lanes)));
                var c = DecodeBatch(new Vector<float>(values.Slice(i + (2 * lanes), lanes)));
                var d = DecodeBatch(new Vector<float>(values.Slice(i + (3 * lanes), lanes)));

                a.CopyTo(values.Slice(i, lanes));
                b.CopyTo(values.Slice(i + lanes, lanes));
                c.CopyTo(values.Slice(i + (2 * lanes), lanes));
                d.CopyTo(values.Slice(i + (3 * lanes), lanes));
            }

            for (; i + lanes <= values.Length; i += lanes) {
                DecodeBatch(new Vector<float>(values.Slice(i, lanes))).CopyTo(values.Slice(i, lanes));
            }
        }

        for (; i < values.Length; i++) {
            values[i] = SrgbToLinear(values[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> DecodeBatch(Vector<float> value)
    {
        var threshold = new Vector<float>(k_SrgbEncodedThreshold);
        var magnitude = Vector.Abs(value);

        var straight = magnitude / new Vector<float>(12.92f);
        var curved = Power((Vector.Max(magnitude, threshold) + new Vector<float>(0.055f)) / new Vector<float>(1.055f), s_DecodeExponent);

        return ApplySign(Vector.ConditionalSelect(Vector.LessThanOrEqual(magnitude, threshold), straight, curved), value, magnitude);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> ApplySign(Vector<float> result, Vector<float> source, Vector<float> magnitude)
    {
        var finite = Vector.LessThan(magnitude, new Vector<float>(float.PositiveInfinity));
        var value = Vector.ConditionalSelect(finite, result, magnitude);
        var negative = Vector.LessThan(source, Vector<float>.Zero);

        return Vector.ConditionalSelect(negative, -value, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> Power(Vector<float> value, PowerExponent exponent)
    {
        var bits = Vector.As<float, uint>(value);
        var binade = Vector.ConvertToSingle(Vector.As<uint, int>(bits >> 23) - new Vector<int>(127));
        var mantissa = Vector.As<uint, float>((bits & new Vector<uint>(0x007FFFFFu)) | new Vector<uint>(0x3F800000u));

        var halved = Vector.GreaterThan(mantissa, new Vector<float>(k_SquareRootOfTwo));
        mantissa = Vector.ConditionalSelect(halved, mantissa * new Vector<float>(0.5f), mantissa);
        binade = Vector.ConditionalSelect(halved, binade + Vector<float>.One, binade);

        var scaled = binade * exponent.Head;
        var whole = Vector.Floor(scaled);
        var y = (scaled - whole) + ((binade * exponent.Tail) + (Logarithm2(mantissa) * exponent.Value));

        return Exponent2(y, whole);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> Logarithm2(Vector<float> mantissa)
    {
        var f = mantissa - Vector<float>.One;
        var square = f * f;

        var series = new Vector<float>(7.0376836292E-2f);
        series = (series * f) - new Vector<float>(1.1514610310E-1f);
        series = (series * f) + new Vector<float>(1.1676998740E-1f);
        series = (series * f) - new Vector<float>(1.2420140846E-1f);
        series = (series * f) + new Vector<float>(1.4249322787E-1f);
        series = (series * f) - new Vector<float>(1.6668057665E-1f);
        series = (series * f) + new Vector<float>(2.0000714765E-1f);
        series = (series * f) - new Vector<float>(2.4999993993E-1f);
        series = (series * f) + new Vector<float>(3.3333331174E-1f);

        var natural = f + ((series * f * square) - (new Vector<float>(0.5f) * square));

        return natural * new Vector<float>(k_InverseLn2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> Exponent2(Vector<float> value, Vector<float> whole)
    {
        var rounded = Vector.Round(value);
        var fraction = value - rounded;

        var series = new Vector<float>(1.535336188319500E-4f);
        series = (series * fraction) + new Vector<float>(1.339887440266574E-3f);
        series = (series * fraction) + new Vector<float>(9.618437357674640E-3f);
        series = (series * fraction) + new Vector<float>(5.550332471162809E-2f);
        series = (series * fraction) + new Vector<float>(2.402264791363012E-1f);
        series = (series * fraction) + new Vector<float>(6.931472028550421E-1f);

        var mantissa = Vector<float>.One + (fraction * series);

        var binade = Vector.ConvertToInt32(whole + rounded);
        var clamped = Vector.Min(Vector.Max(binade, new Vector<int>(-254)), new Vector<int>(254));
        var lower = Vector.ShiftRightArithmetic(clamped, 1);
        var upper = clamped - lower;

        return mantissa * PowerOfTwo(lower) * PowerOfTwo(upper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<float> PowerOfTwo(Vector<int> binade) =>
        Vector.As<uint, float>(Vector.As<int, uint>(binade + new Vector<int>(127)) << 23);

    private readonly struct PowerExponent
    {
        public PowerExponent(float exponent)
        {
            Value = new Vector<float>(exponent);
            var head = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(exponent) & ~0xFFF);
            Head = new Vector<float>(head);
            Tail = new Vector<float>(exponent - head);
        }

        public Vector<float> Value { get; }

        public Vector<float> Head { get; }

        public Vector<float> Tail { get; }
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
