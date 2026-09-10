using Lucitex.Conversion.Kernels;

namespace Lucitex.Tests.Conversion.Kernels;

public class ColorTransformKernelTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(0.0031308f)]
    [InlineData(0.5f)]
    [InlineData(0.18f)]
    public void LinearToSrgb_MatchesIec61966ReferenceFormula(float linear)
    {
        var expected = linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

        float[] values = [linear];
        ColorTransformKernel.LinearToSrgb(values);

        Assert.Equal(expected, values[0], 0.0001f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(0.04045f)]
    [InlineData(0.7f)]
    [InlineData(0.46f)]
    public void SrgbToLinear_MatchesIec61966ReferenceFormula(float srgb)
    {
        var expected = srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);

        float[] values = [srgb];
        ColorTransformKernel.SrgbToLinear(values);

        Assert.Equal(expected, values[0], 0.0001f);
    }

    [Fact]
    public void LinearToSrgbThenBack_RoundTrips()
    {
        var random = new Random(4);
        const int count = 200;
        var original = new float[count];
        for (var i = 0; i < count; i++) {
            original[i] = (float)random.NextDouble();
        }

        var values = (float[])original.Clone();
        ColorTransformKernel.LinearToSrgb(values);
        ColorTransformKernel.SrgbToLinear(values);

        for (var i = 0; i < count; i++) {
            Assert.Equal(original[i], values[i], 0.0001f);
        }
    }

    [Fact]
    public void LinearToSrgb_IsMonotonicallyIncreasing()
    {
        var random = new Random(5);
        var samples = new float[500];
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (float)random.NextDouble();
        }

        Array.Sort(samples);
        var encoded = (float[])samples.Clone();
        ColorTransformKernel.LinearToSrgb(encoded);

        for (var i = 1; i < encoded.Length; i++) {
            Assert.True(encoded[i] >= encoded[i - 1]);
        }
    }

    public static TheoryData<uint, uint> DomainBlocks()
    {
        var data = new TheoryData<uint, uint>();
        uint[] starts = [0x00000000, 0x33000000, 0x38000000, 0x3B000000, 0x3E000000, 0x3F000000, 0x40000000, 0x4B000000, 0x60000000, 0x7F000000];
        foreach (var start in starts) {
            data.Add(start, 1u << 16);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DomainBlocks))]
    public void LinearToSrgb_BatchedPath_StaysWithinEightUlpOfMathFPow(uint start, uint count)
    {
        AssertWithinUlp(start, count, ColorTransformKernel.LinearToSrgb, LinearToSrgbReference);
    }

    [Theory]
    [MemberData(nameof(DomainBlocks))]
    public void SrgbToLinear_BatchedPath_StaysWithinEightUlpOfMathFPow(uint start, uint count)
    {
        AssertWithinUlp(start, count, ColorTransformKernel.SrgbToLinear, SrgbToLinearReference);
    }

    [Fact]
    public void BatchedPath_NonFiniteAndNegativeInputs_MatchScalarReference()
    {
        float[] specials =
        [
            float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -0f,
            -1f, -0.5f, -0.0031308f, -0.04045f, float.MaxValue, -float.MaxValue,
            float.Epsilon, -float.Epsilon, 1e-30f, 1e30f, 3.4e38f,
        ];

        foreach (var (kernel, reference) in new (Action<Span<float>>, Func<float, float>)[] {
            (ColorTransformKernel.LinearToSrgb, LinearToSrgbReference),
            (ColorTransformKernel.SrgbToLinear, SrgbToLinearReference),
        }) {
            var actual = (float[])specials.Clone();
            kernel(actual);

            for (var i = 0; i < specials.Length; i++) {
                var expected = reference(specials[i]);
                if (float.IsFinite(expected) && expected != 0f) {
                    var ulp = Math.Abs(BitConverter.SingleToInt32Bits(expected) - BitConverter.SingleToInt32Bits(actual[i]));
                    Assert.True(ulp <= 8, $"{specials[i]} differs by {ulp} ulp");
                    continue;
                }

                Assert.Equal(BitConverter.SingleToUInt32Bits(expected), BitConverter.SingleToUInt32Bits(actual[i]));
            }
        }
    }

    private static void AssertWithinUlp(uint start, uint count, Action<Span<float>> kernel, Func<float, float> reference)
    {
        var samples = new float[count];
        var expected = new float[count];
        var length = 0;

        for (var i = 0u; i < count; i++) {
            var value = BitConverter.UInt32BitsToSingle(start + i);
            if (!float.IsFinite(value)) {
                continue;
            }

            samples[length] = value;
            expected[length] = reference(value);
            length++;
        }

        var actual = samples.AsSpan(0, length).ToArray();
        kernel(actual);

        for (var i = 0; i < length; i++) {
            var ulp = Math.Abs(BitConverter.SingleToInt32Bits(expected[i]) - BitConverter.SingleToInt32Bits(actual[i]));
            Assert.True(ulp <= 8, $"{samples[i]} (0x{BitConverter.SingleToUInt32Bits(samples[i]):X8}) differs by {ulp} ulp");
        }
    }

    private static float LinearToSrgbReference(float linear)
    {
        var sign = linear < 0f ? -1f : 1f;
        var magnitude = MathF.Abs(linear);
        var encoded = magnitude <= 0.0031308f
            ? magnitude * 12.92f
            : (1.055f * MathF.Pow(magnitude, 1f / 2.4f)) - 0.055f;

        return sign * encoded;
    }

    private static float SrgbToLinearReference(float encoded)
    {
        var sign = encoded < 0f ? -1f : 1f;
        var magnitude = MathF.Abs(encoded);
        var linear = magnitude <= 0.04045f
            ? magnitude / 12.92f
            : MathF.Pow((magnitude + 0.055f) / 1.055f, 2.4f);

        return sign * linear;
    }
}
