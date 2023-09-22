using Lucitex.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class BcImageCodecTests
{
    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public void NonMultipleOfFourDimensions_EncodeAndDecodeWithoutTouchingGuardBytes(int formatValue, int channels)
    {
        var format = (BcFormat)formatValue;
        const int width = 7;
        const int height = 5;
        var source = new byte[width * height * channels];
        new Random(42).NextBytes(source);
        var encodedCount = BcImageCodec.EncodedByteCount(format, width, height);
        var encoded = Enumerable.Repeat((byte)0xCD, encodedCount + 8).ToArray();
        BcImageCodec.Encode(format, source, width, height, encoded.AsSpan(0, encodedCount));
        Assert.All(encoded.AsSpan(encodedCount).ToArray(), value => Assert.Equal(0xCD, value));

        var decoded = Enumerable.Repeat((byte)0xCD, source.Length + 8).ToArray();
        BcImageCodec.Decode(format, encoded, width, height, decoded.AsSpan(0, source.Length));
        Assert.All(decoded.AsSpan(source.Length).ToArray(), value => Assert.Equal(0xCD, value));
    }
}
