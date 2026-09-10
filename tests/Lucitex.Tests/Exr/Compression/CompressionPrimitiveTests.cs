using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Tests.Exr.Compression;

public class CompressionPrimitiveTests
{
    private static byte[] RandomBytes(int length, int seed)
    {
        var buffer = new byte[length];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(33)]
    [InlineData(65)]
    [InlineData(4096)]
    public void BytePredictor_ApplyThenRemove_RestoresOriginalBytes(int length)
    {
        var original = RandomBytes(length, length);
        var working = (byte[])original.Clone();

        BytePredictor.Apply(working);
        BytePredictor.Remove(working);

        Assert.Equal(original, working);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    [InlineData(33)]
    [InlineData(65)]
    [InlineData(4096)]
    public void ByteReorder_SplitThenInterleave_RestoresOriginalBytes(int length)
    {
        var original = RandomBytes(length, length + 1);
        var split = new byte[length];
        var restored = new byte[length];

        ByteReorder.Split(original, split);
        ByteReorder.Interleave(split, restored);

        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(500)]
    [InlineData(8192)]
    public void ExrRle_CompressThenDecompress_RestoresRandomBytes(int length)
    {
        var original = RandomBytes(length, length + 2);

        var compressed = ExrRle.Compress(original);
        var decompressed = new byte[length];
        var written = ExrRle.Decompress(compressed, decompressed);

        Assert.Equal(length, written);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void ExrRle_CompressThenDecompress_RestoresHighlyRepetitiveBytes()
    {
        var original = new byte[10000];
        Array.Fill(original, (byte)0x2A);

        var compressed = ExrRle.Compress(original);
        var decompressed = new byte[original.Length];
        ExrRle.Decompress(compressed, decompressed);

        Assert.Equal(original, decompressed);
        Assert.True(compressed.Length < original.Length / 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(70000)]
    public void ExrZip_CompressThenDecompress_RestoresRandomBytes(int length)
    {
        var original = RandomBytes(length, length + 3);

        var compressed = ExrZip.Compress(original);
        var decompressed = new byte[length];
        ExrZip.Decompress(compressed, decompressed);

        Assert.Equal(original, decompressed);
    }

    [Theory]
    [InlineData(ExrCompressionId.Rle)]
    [InlineData(ExrCompressionId.Zips)]
    [InlineData(ExrCompressionId.Zip)]
    [InlineData(ExrCompressionId.Piz)]
    public void ExrCompressor_IncompressibleInput_FallsBackToOriginalBytes(ExrCompressionId compression)
    {
        var layout = new ExrBlockLayout([new ExrChannelInfo { Name = "R", PixelType = ExrPixelType.Half }], 0, 519, 0, 0);
        var input = new byte[1040];
        new Random(17).NextBytes(input);

        var packed = ExrCompressor.Compress(compression, input, layout);

        Assert.Equal(input, packed);

        var output = new byte[input.Length];
        ExrCompressor.Decompress(compression, packed, output, layout);
        Assert.Equal(input, output);
    }

    [Fact]
    public void ExrHuffman_SkewedDistribution_RoundTripsCodesLongerThanTheDecodeTable()
    {
        var frequencies = new long[24];
        frequencies[0] = 1;
        frequencies[1] = 1;
        for (var i = 2; i < frequencies.Length; i++) {
            frequencies[i] = frequencies[i - 1] + frequencies[i - 2];
        }

        var samples = new List<ushort>();
        for (var symbol = 0; symbol < frequencies.Length; symbol++) {
            for (var i = 0L; i < frequencies[symbol]; i++) {
                samples.Add((ushort)(symbol * 37));
            }
        }

        var shuffled = samples.ToArray();
        var random = new Random(5);
        for (var i = shuffled.Length - 1; i > 0; i--) {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        var compressed = ExrHuffman.Compress(shuffled);
        var decompressed = new ushort[shuffled.Length];
        ExrHuffman.Uncompress(compressed, decompressed);

        Assert.Equal(shuffled, decompressed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public void ExrHuffman_SingleRepeatedSymbol_RoundTrips(int count)
    {
        var samples = new ushort[count];
        Array.Fill(samples, (ushort)4242);

        var compressed = ExrHuffman.Compress(samples);
        var decompressed = new ushort[count];
        ExrHuffman.Uncompress(compressed, decompressed);

        Assert.Equal(samples, decompressed);
    }

    [Fact]
    public void ExrHuffman_RunsLongerThanOneRunLengthCode_RoundTrip()
    {
        var samples = new ushort[3000];
        for (var i = 0; i < samples.Length; i++) {
            samples[i] = (ushort)(i / 700);
        }

        var compressed = ExrHuffman.Compress(samples);
        var decompressed = new ushort[samples.Length];
        ExrHuffman.Uncompress(compressed, decompressed);

        Assert.Equal(samples, decompressed);
        Assert.True(compressed.Length < samples.Length, $"Huffman produced {compressed.Length} bytes for {samples.Length} samples.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(4099)]
    public void ByteReorder_Split_MatchesScalarDefinition(int length)
    {
        var original = RandomBytes(length, length + 11);
        var actual = new byte[length];
        var expected = new byte[length];

        ByteReorder.Split(original, actual);

        var half = (length + 1) / 2;
        var even = 0;
        var odd = half;
        for (var i = 0; i < length; i++) {
            if (i % 2 == 0) {
                expected[even++] = original[i];
            }
            else {
                expected[odd++] = original[i];
            }
        }

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(4099)]
    public void BytePredictor_Remove_MatchesScalarDefinition(int length)
    {
        var original = RandomBytes(length, length + 13);
        var actual = (byte[])original.Clone();
        var expected = (byte[])original.Clone();

        BytePredictor.Remove(actual);

        for (var i = 1; i < expected.Length; i++) {
            expected[i] = unchecked((byte)(expected[i - 1] + expected[i] - 128));
        }

        Assert.Equal(expected, actual);
    }
}
