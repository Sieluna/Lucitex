using Lucitex.Dds.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc4CodecTests
{
    [Fact]
    public void EncodeThenDecode_ExtremeValues_RoundTripsExactly()
    {
        var channel = new byte[16];
        for (var i = 0; i < 16; i++) {
            channel[i] = (byte)(i % 2 == 0 ? 255 : 0);
        }

        Span<byte> block = stackalloc byte[Bc4Codec.BlockByteSize];
        Bc4Codec.Encode(channel, block);

        Span<byte> decoded = stackalloc byte[16];
        Bc4Codec.Decode(block, decoded);

        Assert.True(channel.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_ConstantChannel_RoundTripsExactly()
    {
        var channel = new byte[16];
        Array.Fill(channel, (byte)128);

        Span<byte> block = stackalloc byte[Bc4Codec.BlockByteSize];
        Bc4Codec.Encode(channel, block);

        Span<byte> decoded = stackalloc byte[16];
        Bc4Codec.Decode(block, decoded);

        Assert.True(channel.AsSpan().SequenceEqual(decoded));
    }

    [Fact]
    public void EncodeThenDecode_RandomChannel_StaysWithinReasonableError()
    {
        var random = new Random(7);
        var channel = new byte[16];
        random.NextBytes(channel);

        Span<byte> block = stackalloc byte[Bc4Codec.BlockByteSize];
        Bc4Codec.Encode(channel, block);

        Span<byte> decoded = stackalloc byte[16];
        Bc4Codec.Decode(block, decoded);

        for (var i = 0; i < 16; i++) {
            var error = Math.Abs(channel[i] - decoded[i]);
            Assert.True(error <= 40, $"texel {i} error {error} exceeds bound");
        }
    }
}
