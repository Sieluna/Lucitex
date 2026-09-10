using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Tests.Png.Filtering;

public class PngFilterTests
{
    private static readonly PngFilterType[] s_FilterTypes =
    [
        PngFilterType.None,
        PngFilterType.Sub,
        PngFilterType.Up,
        PngFilterType.Average,
        PngFilterType.Paeth,
    ];

    private static readonly int[] s_BytesPerPixel = [1, 2, 3, 4, 6, 8];

    public static TheoryData<PngFilterType, int> FilterAndBpp()
    {
        var data = new TheoryData<PngFilterType, int>();
        foreach (var filterType in s_FilterTypes) {
            foreach (var bpp in s_BytesPerPixel) {
                data.Add(filterType, bpp);
            }
        }

        return data;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        var buffer = new byte[length];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    [Theory]
    [InlineData(PngFilterType.None)]
    [InlineData(PngFilterType.Sub)]
    [InlineData(PngFilterType.Up)]
    [InlineData(PngFilterType.Average)]
    [InlineData(PngFilterType.Paeth)]
    public void ApplyThenReconstruct_FirstRow_RestoresOriginalBytes(PngFilterType filterType)
    {
        var raw = RandomBytes(37, (int)filterType + 1);
        var filtered = new byte[raw.Length];

        PngFilter.Apply(filterType, filtered, raw, ReadOnlySpan<byte>.Empty, bpp: 4);

        var reconstructed = (byte[])filtered.Clone();
        PngFilter.Reconstruct(filterType, reconstructed, ReadOnlySpan<byte>.Empty, bpp: 4);

        Assert.Equal(raw, reconstructed);
    }

    [Theory]
    [InlineData(PngFilterType.None)]
    [InlineData(PngFilterType.Sub)]
    [InlineData(PngFilterType.Up)]
    [InlineData(PngFilterType.Average)]
    [InlineData(PngFilterType.Paeth)]
    public void ApplyThenReconstruct_WithPreviousRow_RestoresOriginalBytes(PngFilterType filterType)
    {
        var previousRaw = RandomBytes(37, 100 + (int)filterType);
        var raw = RandomBytes(37, 200 + (int)filterType);
        var filtered = new byte[raw.Length];

        PngFilter.Apply(filterType, filtered, raw, previousRaw, bpp: 3);

        var reconstructed = (byte[])filtered.Clone();
        PngFilter.Reconstruct(filterType, reconstructed, previousRaw, bpp: 3);

        Assert.Equal(raw, reconstructed);
    }

    [Theory]
    [MemberData(nameof(FilterAndBpp))]
    public void ApplyThenReconstruct_AcrossRowLengthsAndPixelStrides_RestoresOriginalBytes(PngFilterType filterType, int bpp)
    {
        foreach (var pixels in Enumerable.Range(1, 40).Append(257)) {
            var length = pixels * bpp;
            var previousRaw = RandomBytes(length, (length * 31) + bpp);
            var raw = RandomBytes(length, (length * 17) + bpp + 1);
            var filtered = new byte[length];

            PngFilter.Apply(filterType, filtered, raw, previousRaw, bpp);

            var reconstructed = (byte[])filtered.Clone();
            PngFilter.Reconstruct(filterType, reconstructed, previousRaw, bpp);

            Assert.Equal(raw, reconstructed);

            PngFilter.Apply(filterType, filtered, raw, ReadOnlySpan<byte>.Empty, bpp);

            reconstructed = (byte[])filtered.Clone();
            PngFilter.Reconstruct(filterType, reconstructed, ReadOnlySpan<byte>.Empty, bpp);

            Assert.Equal(raw, reconstructed);
        }
    }

    [Theory]
    [MemberData(nameof(FilterAndBpp))]
    public void Reconstruct_MatchesScalarReference(PngFilterType filterType, int bpp)
    {
        var length = (bpp * 101) + 3;
        var previousRaw = RandomBytes(length, 4200 + bpp);
        var filtered = RandomBytes(length, 4300 + bpp);

        var actual = (byte[])filtered.Clone();
        PngFilter.Reconstruct(filterType, actual, previousRaw, bpp);

        Assert.Equal(ReconstructReference(filterType, filtered, previousRaw, bpp), actual);
    }

    private static byte[] ReconstructReference(PngFilterType filterType, byte[] filtered, byte[] previous, int bpp)
    {
        var current = (byte[])filtered.Clone();

        for (var i = 0; i < current.Length; i++) {
            var left = i >= bpp ? current[i - bpp] : 0;
            var up = previous[i];
            var upLeft = i >= bpp ? previous[i - bpp] : 0;

            current[i] = filterType switch {
                PngFilterType.None => current[i],
                PngFilterType.Sub => unchecked((byte)(current[i] + left)),
                PngFilterType.Up => unchecked((byte)(current[i] + up)),
                PngFilterType.Average => unchecked((byte)(current[i] + ((left + up) / 2))),
                PngFilterType.Paeth => unchecked((byte)(current[i] + PaethReference(left, up, upLeft))),
                _ => throw new ArgumentOutOfRangeException(nameof(filterType)),
            };
        }

        return current;
    }

    private static int PaethReference(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) {
            return a;
        }

        return pb <= pc ? b : c;
    }

    [Fact]
    public void Reconstruct_UnknownFilterType_Throws()
    {
        var buffer = new byte[8];
        Assert.Throws<Lucitex.Core.Execution.ImageFormatException>(() =>
            PngFilter.Reconstruct((PngFilterType)99, buffer, ReadOnlySpan<byte>.Empty, bpp: 1));
    }
}
