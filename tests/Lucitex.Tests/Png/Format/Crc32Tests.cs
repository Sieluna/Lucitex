using System.Text;
using Lucitex.Png.Format;

namespace Lucitex.Tests.Png.Format;

public class Crc32Tests
{
    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("a", 0xE8B7BE43u)]
    [InlineData("123456789", 0xCBF43926u)]
    [InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
    public void Compute_MatchesPublishedCheckValues(string text, uint expected) =>
        Assert.Equal(expected, Crc32.Compute(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void Compute_AcrossLengthsSpanningTheBatchedThreshold_MatchesBitwiseReference()
    {
        var random = new Random(17);

        foreach (var length in Enumerable.Range(0, 200).Concat([255, 256, 1023, 4096, 65_537])) {
            var data = new byte[length];
            random.NextBytes(data);

            Assert.Equal(BitwiseReference(data), Crc32.Compute(data));
        }
    }

    [Fact]
    public void Compute_SplitAcrossTwoSpans_MatchesTheConcatenation()
    {
        var random = new Random(19);
        var data = new byte[5000];
        random.NextBytes(data);

        foreach (var split in new[] { 0, 1, 17, 63, 64, 65, 512, 4999, 5000 }) {
            Assert.Equal(Crc32.Compute(data), Crc32.Compute(data.AsSpan(0, split), data.AsSpan(split)));
        }
    }

    private static uint BitwiseReference(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data) {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
