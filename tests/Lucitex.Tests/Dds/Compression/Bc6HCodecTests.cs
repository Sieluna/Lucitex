using Lucitex.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class Bc6HCodecTests
{
    [Theory]
    [InlineData("Lucitex.Tests.Bc6HUnsignedConformance.bin", false)]
    [InlineData("Lucitex.Tests.Bc6HSignedConformance.bin", true)]
    public void ReferenceVectors_MatchEveryMode(string resourceName, bool signed)
    {
        using var stream = typeof(Bc6HCodecTests).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);
        var recordCount = reader.ReadInt32();
        var seenModes = new HashSet<byte>();
        Span<float> actual = stackalloc float[48];

        for (var record = 0; record < recordCount; record++) {
            var mode = reader.ReadByte();
            var encoded = reader.ReadBytes(16);
            Bc6HCodec.Decode(encoded, actual, signed);
            for (var value = 0; value < actual.Length; value++) {
                Assert.Equal(reader.ReadSingle(), actual[value]);
            }

            seenModes.Add(mode);
        }

        Assert.Equal(new byte[] { 0, 1, 2, 3, 6, 7, 10, 11, 14, 15, 18, 22, 26, 30 }, seenModes.Order());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void PartialImage_DecodesWithoutTouchingGuardValues()
    {
        const int width = 7;
        const int height = 5;
        var encoded = new byte[Bc6HImageCodec.EncodedByteCount(width, height)];
        var decodedLength = width * height * 3;
        var decoded = Enumerable.Repeat(1234f, decodedLength + 16).ToArray();
        Bc6HImageCodec.Decode(encoded, width, height, signed: false, decoded.AsSpan(0, decodedLength));

        Assert.All(decoded.AsSpan(decodedLength).ToArray(), value => Assert.Equal(1234f, value));
    }

    [Fact]
    public void InvalidMode_DecodesToBlack()
    {
        var encoded = Enumerable.Repeat((byte)0xFF, 16).ToArray();
        Span<float> decoded = stackalloc float[48];
        Bc6HCodec.Decode(encoded, decoded, signed: false);
        Assert.All(decoded.ToArray(), value => Assert.Equal(0f, value));
    }
}
