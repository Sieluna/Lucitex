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
}
