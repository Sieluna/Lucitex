using Lucitex.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc2CodecTests
{
    [Fact]
    public void EncodeThenDecode_ExplicitAlphaAndTwoColors_RoundTripsExactly()
    {
        var rgba = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            var red = texel % 2 == 0;
            rgba[texel * 4] = red ? (byte)255 : (byte)0;
            rgba[(texel * 4) + 1] = red ? (byte)0 : (byte)255;
            rgba[(texel * 4) + 2] = 0;
            rgba[(texel * 4) + 3] = (byte)(texel * 17);
        }

        Span<byte> block = stackalloc byte[Bc2Codec.BlockByteSize];
        Bc2Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc2Codec.Decode(block, decoded);

        Assert.True(rgba.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_RandomAlpha_StaysWithinFourBitQuantizationError()
    {
        var rgba = new byte[64];
        new Random(41).NextBytes(rgba);

        Span<byte> block = stackalloc byte[Bc2Codec.BlockByteSize];
        Bc2Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc2Codec.Decode(block, decoded);

        for (var texel = 0; texel < 16; texel++) {
            var offset = (texel * 4) + 3;
            Assert.True(Math.Abs(rgba[offset] - decoded[offset]) <= 8);
        }
    }
}
