using Lucitex.Jpeg;

namespace Lucitex.Tests.Jpeg;

public class DctTests
{
    private const float k_Tolerance = 1e-3f;

    private static void ReferenceTransform(ReadOnlySpan<float> input, Span<float> output, bool inverse)
    {
        for (var y = 0; y < 8; y++) {
            for (var x = 0; x < 8; x++) {
                double sum = 0;
                for (var v = 0; v < 8; v++) {
                    for (var u = 0; u < 8; u++) {
                        var vertical = inverse ? Basis(y, v) : Basis(v, y);
                        var horizontal = inverse ? Basis(x, u) : Basis(u, x);
                        sum += input[v * 8 + u] * vertical * horizontal;
                    }
                }
                output[y * 8 + x] = (float)sum;
            }
        }

        static double Basis(int position, int frequency) =>
            0.5 * (frequency == 0 ? 1 / Math.Sqrt(2) : 1) * Math.Cos((2 * position + 1) * frequency * Math.PI / 16);
    }

    [Fact]
    public void ForwardDct_MatchesDefinition_OnRandomBlocks()
    {
        var random = new Random(101);
        var samples = new float[64];
        var expected = new float[64];
        var actual = new float[64];

        for (var trial = 0; trial < 200; trial++) {
            for (var i = 0; i < 64; i++) {
                samples[i] = (float)((random.NextDouble() * 255.0) - 128.0);
            }

            ReferenceTransform(samples, expected, inverse: false);
            DctTransform.Forward(samples, actual);

            for (var i = 0; i < 64; i++) {
                Assert.True(
                    MathF.Abs(expected[i] - actual[i]) <= k_Tolerance,
                    $"index {i}: expected={expected[i]}, actual={actual[i]}");
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(63)]
    public void InverseDct_MatchesDefinition_OnSparseBlocks(int nonZeroCount)
    {
        var random = new Random(202 + nonZeroCount);
        var coefficients = new int[64];
        var expected = new float[64];
        var actual = new float[64];

        for (var trial = 0; trial < 50; trial++) {
            Array.Clear(coefficients);
            var indices = Enumerable.Range(0, 64).OrderBy(_ => random.Next()).Take(nonZeroCount);
            foreach (var index in indices) {
                coefficients[index] = random.Next(-1024, 1025);
            }

            ReferenceTransform(Array.ConvertAll(coefficients, value => (float)value), expected, inverse: true);
            DctTransform.Inverse(coefficients, actual);

            for (var i = 0; i < 64; i++) {
                Assert.True(
                    MathF.Abs(expected[i] - actual[i]) <= k_Tolerance,
                    $"index {i}: expected={expected[i]}, actual={actual[i]}");
            }
        }
    }

    [Fact]
    public void InverseDct_MatchesDefinition_OnDenseRandomBlocks()
    {
        var random = new Random(303);
        var coefficients = new int[64];
        var expected = new float[64];
        var actual = new float[64];

        for (var trial = 0; trial < 200; trial++) {
            for (var i = 0; i < 64; i++) {
                coefficients[i] = random.Next(-512, 513);
            }

            ReferenceTransform(Array.ConvertAll(coefficients, value => (float)value), expected, inverse: true);
            DctTransform.Inverse(coefficients, actual);

            for (var i = 0; i < 64; i++) {
                Assert.True(
                    MathF.Abs(expected[i] - actual[i]) <= k_Tolerance,
                    $"index {i}: expected={expected[i]}, actual={actual[i]}");
            }
        }
    }
}
