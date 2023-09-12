using Lucitex.TextureCompression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc1CodecTests
{
    private static byte[] SolidColorBlock(byte r, byte g, byte b, byte a)
    {
        var block = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            block[texel * 4] = r;
            block[(texel * 4) + 1] = g;
            block[(texel * 4) + 2] = b;
            block[(texel * 4) + 3] = a;
        }

        return block;
    }

    [Fact]
    public void EncodeThenDecode_TwoExactlyRepresentableColors_RoundTripsExactly()
    {
        var rgba = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            var useRed = texel % 2 == 0;
            rgba[texel * 4] = (byte)(useRed ? 255 : 0);
            rgba[(texel * 4) + 1] = (byte)(useRed ? 0 : 255);
            rgba[(texel * 4) + 2] = 0;
            rgba[(texel * 4) + 3] = 255;
        }

        Span<byte> block = stackalloc byte[Bc1Codec.BlockByteSize];
        Bc1Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc1Codec.Decode(block, decoded);

        Assert.True(rgba.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_SolidColor_RoundTripsExactly()
    {
        var rgba = SolidColorBlock(255, 255, 255, 255);

        Span<byte> block = stackalloc byte[Bc1Codec.BlockByteSize];
        Bc1Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc1Codec.Decode(block, decoded);

        Assert.True(rgba.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_SmoothGradientBlock_StaysWithinReasonableError()
    {
        var random = new Random(42);
        var from = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        var to = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

        var rgba = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            var t = texel / 15.0;
            rgba[texel * 4] = (byte)Math.Round(from.Item1 + ((to.Item1 - from.Item1) * t));
            rgba[(texel * 4) + 1] = (byte)Math.Round(from.Item2 + ((to.Item2 - from.Item2) * t));
            rgba[(texel * 4) + 2] = (byte)Math.Round(from.Item3 + ((to.Item3 - from.Item3) * t));
            rgba[(texel * 4) + 3] = 255;
        }

        Span<byte> block = stackalloc byte[Bc1Codec.BlockByteSize];
        Bc1Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc1Codec.Decode(block, decoded);

        for (var texel = 0; texel < 16; texel++) {
            for (var channel = 0; channel < 3; channel++) {
                var offset = (texel * 4) + channel;
                var error = Math.Abs(rgba[offset] - decoded[offset]);
                Assert.True(error <= 40, $"texel {texel} channel {channel} error {error} exceeds bound");
            }
        }
    }

    [Fact]
    public void ForceOpaqueMode_NeverProducesPunchThroughTransparency()
    {
        var rgba = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            rgba[texel * 4] = 10;
            rgba[(texel * 4) + 1] = 10;
            rgba[(texel * 4) + 2] = 10;
            rgba[(texel * 4) + 3] = 255;
        }

        rgba[0] = 12;

        Span<byte> block = stackalloc byte[Bc1Codec.BlockByteSize];
        Bc1Codec.Encode(rgba, block, forceOpaqueMode: true);

        Span<byte> decoded = stackalloc byte[64];
        Bc1Codec.Decode(block, decoded);

        for (var texel = 0; texel < 16; texel++) {
            Assert.Equal(255, decoded[(texel * 4) + 3]);
        }
    }
}
