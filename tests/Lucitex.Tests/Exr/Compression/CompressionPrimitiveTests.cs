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
    public void ExrCompressor_IncompressibleInput_FallsBackToOriginalBytes(ExrCompressionId compression)
    {
        var input = new byte[1040];
        new Random(17).NextBytes(input);

        var packed = ExrCompressor.Compress(compression, input);

        Assert.Equal(input, packed);

        var output = new byte[input.Length];
        ExrCompressor.Decompress(compression, packed, output);
        Assert.Equal(input, output);
    }
}
