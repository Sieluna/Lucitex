using Lucitex.Dds.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc3CodecTests
{
    [Fact]
    public void EncodeThenDecode_ExactColorsAndAlpha_RoundTripsExactly()
    {
        var rgba = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            var useRed = texel % 2 == 0;
            rgba[texel * 4] = (byte)(useRed ? 255 : 0);
            rgba[(texel * 4) + 1] = (byte)(useRed ? 0 : 255);
            rgba[(texel * 4) + 2] = 0;
            rgba[(texel * 4) + 3] = (byte)(useRed ? 255 : 0);
        }

        Span<byte> block = stackalloc byte[Bc3Codec.BlockByteSize];
        Bc3Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc3Codec.Decode(block, decoded);

        Assert.True(rgba.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_SmoothGradientBlock_StaysWithinReasonableError()
    {
        var random = new Random(99);
        var fromColor = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        var toColor = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

        var rgba = new byte[64];
        for (var texel = 0; texel < 16; texel++) {
            var t = texel / 15.0;
            rgba[texel * 4] = (byte)Math.Round(fromColor.Item1 + ((toColor.Item1 - fromColor.Item1) * t));
            rgba[(texel * 4) + 1] = (byte)Math.Round(fromColor.Item2 + ((toColor.Item2 - fromColor.Item2) * t));
            rgba[(texel * 4) + 2] = (byte)Math.Round(fromColor.Item3 + ((toColor.Item3 - fromColor.Item3) * t));
            rgba[(texel * 4) + 3] = (byte)Math.Round(fromColor.Item4 + ((toColor.Item4 - fromColor.Item4) * t));
        }

        Span<byte> block = stackalloc byte[Bc3Codec.BlockByteSize];
        Bc3Codec.Encode(rgba, block);

        Span<byte> decoded = stackalloc byte[64];
        Bc3Codec.Decode(block, decoded);

        for (var i = 0; i < 64; i++) {
            var error = Math.Abs(rgba[i] - decoded[i]);
            Assert.True(error <= 40, $"byte {i} error {error} exceeds bound");
        }
    }
}
