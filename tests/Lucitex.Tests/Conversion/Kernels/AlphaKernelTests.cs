using Lucitex.Conversion.Kernels;

namespace Lucitex.Tests.Conversion.Kernels;

public class AlphaKernelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(129)]
    public void Premultiply_MatchesScalarReference_AtVectorAndScalarPathSizes(int count)
    {
        var random = new Random(1);
        var color = new float[count];
        var alpha = new float[count];
        for (var i = 0; i < count; i++) {
            color[i] = (float)random.NextDouble();
            alpha[i] = (float)random.NextDouble();
        }

        var expected = new float[count];
        for (var i = 0; i < count; i++) {
            expected[i] = color[i] * alpha[i];
        }

        AlphaKernel.Premultiply(color, alpha);

        for (var i = 0; i < count; i++) {
            Assert.Equal(expected[i], color[i], 0.000001f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(129)]
    public void Unpremultiply_MatchesScalarReference_AtVectorAndScalarPathSizes(int count)
    {
        var random = new Random(2);
        var color = new float[count];
        var alpha = new float[count];
        for (var i = 0; i < count; i++) {
            color[i] = (float)random.NextDouble();
            alpha[i] = (float)random.NextDouble();
        }

        alpha[0] = 0f;

        var expected = new float[count];
        for (var i = 0; i < count; i++) {
            expected[i] = alpha[i] == 0f ? 0f : color[i] / alpha[i];
        }

        AlphaKernel.Unpremultiply(color, alpha);

        for (var i = 0; i < count; i++) {
            Assert.Equal(expected[i], color[i], 0.000001f);
        }
    }

    [Fact]
    public void PremultiplyThenUnpremultiply_RoundTrips_WhenAlphaIsNonZero()
    {
        var random = new Random(3);
        const int count = 64;
        var original = new float[count];
        var alpha = new float[count];
        for (var i = 0; i < count; i++) {
            original[i] = (float)random.NextDouble();
            alpha[i] = 0.01f + ((float)random.NextDouble() * 0.99f);
        }

        var color = (float[])original.Clone();
        AlphaKernel.Premultiply(color, alpha);
        AlphaKernel.Unpremultiply(color, alpha);

        for (var i = 0; i < count; i++) {
            Assert.Equal(original[i], color[i], 0.0001f);
        }
    }

    [Fact]
    public void Unpremultiply_ZeroAlpha_ProducesZero()
    {
        float[] color = [0.5f, 0.75f];
        float[] alpha = [0f, 0f];

        AlphaKernel.Unpremultiply(color, alpha);

        Assert.Equal(0f, color[0]);
        Assert.Equal(0f, color[1]);
    }
}
