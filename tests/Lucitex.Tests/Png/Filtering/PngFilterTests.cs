using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Tests.Png.Filtering;

public class PngFilterTests
{
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

    [Fact]
    public void Reconstruct_UnknownFilterType_Throws()
    {
        var buffer = new byte[8];
        Assert.Throws<Lucitex.Core.Execution.ImageFormatException>(() =>
            PngFilter.Reconstruct((PngFilterType)99, buffer, ReadOnlySpan<byte>.Empty, bpp: 1));
    }
}
