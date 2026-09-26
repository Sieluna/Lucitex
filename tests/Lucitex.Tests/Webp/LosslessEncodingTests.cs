using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Tests.Fixtures;
using Lucitex.Webp;

// A smooth ramp compresses extremely well once predicted, easily past DecodeLimits' default
// MaxCompressionRatio (a decompression-bomb guard, not a correctness signal) - these tests raise it.

namespace Lucitex.Tests.Webp;

public class LosslessEncodingTests
{
    private static WorkRegion FullRegion(long width, long height) => new() {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    // A diagonal gradient/ramp: smooth horizontally and vertically, exactly the content the gradient
    // predictors (mode 7 "Average(left, top)" in particular) target.
    private static byte[] RampRgba(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var i = ((y * width) + x) * 4;
                var h = (byte)((x * 255) / Math.Max(1, width - 1));
                var v = (byte)((y * 255) / Math.Max(1, height - 1));
                pixels[i] = h;
                pixels[i + 1] = v;
                pixels[i + 2] = (byte)((h + v) / 2);
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] EncodeLossless(int width, int height, byte[] pixels, WebpCompressionEffort effort)
    {
        using var stream = new MemoryStream();
        var codec = new WebpCodec();
        var writer = codec.CreateWriter(stream, PngFixtures.Rgba8(width, height), new WebpEncoderOptions { Lossless = true, Effort = effort });
        writer.Write(FullRegion(width, height), pixels);
        writer.Finish();
        return stream.ToArray();
    }

    [Theory]
    [InlineData(WebpCompressionEffort.Fast)]
    [InlineData(WebpCompressionEffort.Balanced)]
    public void RoundTrip_Ramp_IsLossless(WebpCompressionEffort effort)
    {
        const int width = 128, height = 128;
        var pixels = RampRgba(width, height);

        var encoded = EncodeLossless(width, height, pixels, effort);

        using var stream = new MemoryStream(encoded);
        var reader = new WebpCodec().OpenReader(stream, new DecodeLimits { MaxCompressionRatio = double.MaxValue });
        var decoded = new byte[pixels.Length];
        reader.Read(FullRegion(width, height), decoded);

        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void FastEffort_OnRampContent_StillCompressesWellBelowRawSize()
    {
        // Fast now runs a cheap real predictor search instead of skipping prediction outright, since
        // gradient content specifically loses a lot of size when left unpredicted.
        const int width = 128, height = 128;
        var pixels = RampRgba(width, height);

        var encoded = EncodeLossless(width, height, pixels, WebpCompressionEffort.Fast);

        Assert.True(encoded.Length < pixels.Length / 2,
            $"Expected Fast-effort lossless encoding of ramp content to compress below half the raw size ({pixels.Length} bytes), got {encoded.Length} bytes.");
    }
}
