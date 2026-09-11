using System.Security.Cryptography;
using Lucitex.Compression;

namespace Lucitex.Tests.Dds.Compression;

public class BcEncodeStabilityTests
{
    private const int k_Width = 128;
    private const int k_Height = 128;

    [Theory]
    [InlineData("Bc1")]
    [InlineData("Bc2")]
    [InlineData("Bc3")]
    [InlineData("Bc4")]
    [InlineData("Bc5")]
    public void Encode_ProducesTheSameBytesOnEveryInstructionSetPath(string formatName)
    {
        var format = Enum.Parse<BcFormat>(formatName);
        var channels = BcImageCodec.ChannelCount(format);
        var pixels = new byte[k_Width * k_Height * channels];
        Fill(pixels, channels);

        var encoded = new byte[BcImageCodec.EncodedByteCount(format, k_Width, k_Height)];
        BcImageCodec.Encode(format, pixels, k_Width, k_Height, encoded);

        Assert.Equal(Expected(format), Convert.ToHexString(SHA256.HashData(encoded))[..32]);
    }

    private static string Expected(BcFormat format) => format switch {
        BcFormat.Bc1 => "1EEF013FA8004DD9E9045F3343E8DA95",
        BcFormat.Bc2 => "6128D9BDC6AA842EA254AC9833FFEACF",
        BcFormat.Bc3 => "AFE98488D7DE6ACA0F7D2C860EBB874F",
        BcFormat.Bc4 => "6A797FDF5CDD0E8D1AA438E6DFB45405",
        BcFormat.Bc5 => "1B09D1BA9EE620B06C195CFBDD5A9FFC",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static void Fill(Span<byte> pixels, int channels)
    {
        var random = new Random(23);

        for (var y = 0; y < k_Height; y++) {
            for (var x = 0; x < k_Width; x++) {
                var offset = ((y * k_Width) + x) * channels;
                var smooth = (x / 17) % 3 == 0;
                for (var channel = 0; channel < channels; channel++) {
                    pixels[offset + channel] = smooth
                        ? (byte)((x * 3) + (y * 5) + (channel * 29))
                        : (byte)random.Next(256);
                }
            }
        }
    }
}
