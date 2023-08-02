using Lucitex.Png.Filtering;

namespace Lucitex.Tests.Png.Filtering;

public class Adam7Tests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(37, 23)]
    [InlineData(1, 50)]
    [InlineData(50, 1)]
    [InlineData(3, 3)]
    public void PassDimensions_SumOfPassPixelCounts_EqualsTotalPixelCount(int width, int height)
    {
        var total = 0L;
        for (var pass = 0; pass < 7; pass++)
        {
            var (passWidth, passHeight) = Adam7.PassDimensions(width, height, pass);
            total += (long)passWidth * passHeight;
        }

        Assert.Equal((long)width * height, total);
    }

    [Fact]
    public void PassDimensions_8x8Image_MatchesKnownReferenceValues()
    {
        (int Width, int Height)[] expected =
        [
            (1, 1),
            (1, 1),
            (2, 1),
            (2, 2),
            (4, 2),
            (4, 4),
            (8, 4),
        ];

        for (var pass = 0; pass < 7; pass++)
        {
            var actual = Adam7.PassDimensions(8, 8, pass);
            Assert.Equal(expected[pass], actual);
        }
    }

    [Fact]
    public void PassDimensions_TinyImage_SkipsPassesWithZeroExtent()
    {
        var (width, height) = Adam7.PassDimensions(1, 1, 0);
        Assert.Equal((1, 1), (width, height));

        for (var pass = 1; pass < 7; pass++)
        {
            var (w, h) = Adam7.PassDimensions(1, 1, pass);
            Assert.True(w == 0 || h == 0);
        }
    }
}
