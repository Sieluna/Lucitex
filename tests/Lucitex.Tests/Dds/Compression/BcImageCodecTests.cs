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

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public void ArrayOverloads_MatchTheSpanOverloadsOverTheParallelThreshold(int formatValue, int channels)
    {
        var format = (BcFormat)formatValue;
        const int width = 132;
        const int height = 68;
        var source = new byte[width * height * channels];
        new Random(7).NextBytes(source);

        var encodedCount = BcImageCodec.EncodedByteCount(format, width, height);
        var serialEncoded = new byte[encodedCount];
        var parallelEncoded = new byte[encodedCount];
        BcImageCodec.Encode(format, source.AsSpan(), width, height, serialEncoded.AsSpan());
        BcImageCodec.Encode(format, source, width, height, parallelEncoded);
        Assert.Equal(serialEncoded, parallelEncoded);

        var serialDecoded = new byte[source.Length];
        var parallelDecoded = new byte[source.Length];
        BcImageCodec.Decode(format, serialEncoded.AsSpan(), width, height, serialDecoded.AsSpan());
        BcImageCodec.Decode(format, serialEncoded, width, height, parallelDecoded);
        Assert.Equal(serialDecoded, parallelDecoded);
    }
}
