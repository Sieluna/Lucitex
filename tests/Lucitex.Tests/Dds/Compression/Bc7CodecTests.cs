using Lucitex.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc7CodecTests
{
    [Fact]
    public void ReferenceVectors_MatchEveryModePartitionRotationAndIndexMode()
    {
        using var stream = typeof(Bc7CodecTests).Assembly.GetManifestResourceStream("Lucitex.Tests.Bc7Conformance.bin");
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);
        var recordCount = reader.ReadInt32();
        var seenModes = new HashSet<byte>();
        Span<byte> actual = stackalloc byte[64];

        for (var i = 0; i < recordCount; i++) {
            var mode = reader.ReadByte();
            var encoded = reader.ReadBytes(16);
            var expected = reader.ReadBytes(64);
            Bc7Codec.Decode(encoded, actual);
            Assert.True(actual.SequenceEqual(expected), $"BC7 reference mismatch for mode {mode}, record {i}.");
            seenModes.Add(mode);
        }

        Assert.Equal(Enumerable.Range(0, 8).Select(value => (byte)value), seenModes.Order());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void ReservedMode_DecodesToOpaqueBlack()
    {
        Span<byte> actual = stackalloc byte[64];
        Bc7Codec.Decode(new byte[16], actual);

        for (var pixel = 0; pixel < 16; pixel++) {
            Assert.Equal(0, actual[(pixel * 4) + 0]);
            Assert.Equal(0, actual[(pixel * 4) + 1]);
            Assert.Equal(0, actual[(pixel * 4) + 2]);
            Assert.Equal(255, actual[(pixel * 4) + 3]);
        }
    }

    [Fact]
    public void PartialImage_DecodesWithoutTouchingGuardBytes()
    {
        const int width = 7;
        const int height = 5;
        var encoded = new byte[BcImageCodec.EncodedByteCount(BcFormat.Bc7, width, height)];
        for (var block = 0; block < encoded.Length / 16; block++) {
            encoded[block * 16] = 0x40;
        }

        var decodedLength = width * height * 4;
        var decoded = Enumerable.Repeat((byte)0xCD, decodedLength + 16).ToArray();
        BcImageCodec.Decode(BcFormat.Bc7, encoded, width, height, decoded.AsSpan(0, decodedLength));

        Assert.All(decoded.AsSpan(decodedLength).ToArray(), value => Assert.Equal(0xCD, value));
    }
}
