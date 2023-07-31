using Lucitex.Png.Filtering;

namespace Lucitex.Tests.Phase3;

public class PngBitPackingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void WriteThenReadSample_RoundTripsAllValuesForBitDepth(int bitDepth)
    {
        const int sampleCount = 37;
        var maxValue = (1u << bitDepth) - 1;
        var rowBytes = ((sampleCount * bitDepth) + 7) / 8;
        var row = new byte[rowBytes];

        var expected = new uint[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            expected[i] = (uint)(i % (maxValue + 1));
            PngBitPacking.WriteSample(row, i, bitDepth, expected[i]);
        }

        for (var i = 0; i < sampleCount; i++)
        {
            Assert.Equal(expected[i], PngBitPacking.ReadSample(row, i, bitDepth));
        }
    }

    [Fact]
    public void WriteSample_DoesNotDisturbAdjacentSamplesInSameByte()
    {
        var row = new byte[1];
        PngBitPacking.WriteSample(row, 0, 2, 0b11);
        PngBitPacking.WriteSample(row, 1, 2, 0b01);
        PngBitPacking.WriteSample(row, 2, 2, 0b10);
        PngBitPacking.WriteSample(row, 3, 2, 0b00);

        Assert.Equal(0b11u, PngBitPacking.ReadSample(row, 0, 2));
        Assert.Equal(0b01u, PngBitPacking.ReadSample(row, 1, 2));
        Assert.Equal(0b10u, PngBitPacking.ReadSample(row, 2, 2));
        Assert.Equal(0b00u, PngBitPacking.ReadSample(row, 3, 2));
    }
}
