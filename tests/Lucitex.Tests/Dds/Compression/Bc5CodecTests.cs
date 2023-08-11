using Lucitex.Dds.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc5CodecTests
{
    [Fact]
    public void EncodeThenDecode_ExtremeValues_RoundTripsExactly()
    {
        var rg = new byte[32];
        for (var texel = 0; texel < 16; texel++) {
            rg[texel * 2] = (byte)(texel % 2 == 0 ? 255 : 0);
            rg[(texel * 2) + 1] = (byte)(texel % 3 == 0 ? 255 : 0);
        }

        Span<byte> block = stackalloc byte[Bc5Codec.BlockByteSize];
        Bc5Codec.Encode(rg, block);

        Span<byte> decoded = stackalloc byte[32];
        Bc5Codec.Decode(block, decoded);

        Assert.True(rg.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_RandomChannels_StaysWithinReasonableError()
    {
        var random = new Random(13);
        var rg = new byte[32];
        random.NextBytes(rg);

        Span<byte> block = stackalloc byte[Bc5Codec.BlockByteSize];
        Bc5Codec.Encode(rg, block);

        Span<byte> decoded = stackalloc byte[32];
        Bc5Codec.Decode(block, decoded);

        for (var i = 0; i < 32; i++) {
            var error = Math.Abs(rg[i] - decoded[i]);
            Assert.True(error <= 40, $"byte {i} error {error} exceeds bound");
        }
    }
}
