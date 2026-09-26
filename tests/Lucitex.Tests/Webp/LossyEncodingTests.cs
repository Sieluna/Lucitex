using System.Buffers.Binary;
using Lucitex.Core.Execution;
using Lucitex.Core.Metadata;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Tests.Fixtures;
using Lucitex.Webp;
using Lucitex.Webp.Lossy;

namespace Lucitex.Tests.Webp;

public class LossyEncodingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (width, height) in new[] { (1, 1), (1, 17), (19, 1), (17, 19), (64, 48) }) {
            foreach (var quality in new[] { 0, 25, 75, 100 }) {
                foreach (var effort in Enum.GetValues<WebpCompressionEffort>()) {
                    foreach (var alpha in new[] { false, true }) {
                        yield return [width, height, quality, effort, alpha];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Lossy_RoundTrip_PreservesDimensionsAndAlpha(int width, int height, int quality, WebpCompressionEffort effort, bool alpha)
    {
        var pixels = Pixels(width, height, alpha);
        var encoded = Encode(width, height, pixels, new() { Lossless = false, Quality = quality, Effort = effort });
        Assert.Equal(encoded.Length - 8, (long)BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)));
        using var source = new MemoryStream(encoded);
        using var reader = new WebpCodec().OpenReader(source);
        Assert.Equal(new Extent3L(width, height, 1), reader.Describe().Parts[0].Topology.BaseExtent);
        var decoded = new byte[pixels.Length];
        Assert.Equal(decoded.Length, reader.Read(Region(width, height), decoded));
        for (var i = 3; i < pixels.Length; i += 4) {
            Assert.Equal(pixels[i], decoded[i]);
        }
        var memory = new WebpMemory(16 * 1024 * 1024);
        using var container = new MemoryStream(encoded);
        var document = WebpContainer.Read(container, DecodeLimits.Default, memory);
        using var payload = document.Payload;
        using var alphaPayload = document.AlphaPayload;
        Assert.True(document.IsLossy);
        Assert.Equal(alpha && pixels.Where((_, index) => index % 4 == 3).Any(value => value != 255), alphaPayload is not null);
        var header = Vp8FrameHeader.ParseUncompressed(payload.Span, out var size, out var offset);
        header.ParseCompressed(new Vp8BoolDecoder(payload.Span.Slice(offset, size).ToArray()));
        Assert.Equal((127 * (100 - quality) + 50) / 100, header.Quant.YacQi);
    }

    [Fact]
    public void Quality_IncreasesSizeAndReducesDistortion()
    {
        const int width = 97, height = 65;
        var pixels = Pixels(width, height, false);
        var low = Encode(width, height, pixels, new() { Lossless = false, Quality = 10 });
        var high = Encode(width, height, pixels, new() { Lossless = false, Quality = 95 });
        Assert.True(high.Length > low.Length);
        Assert.True(Error(high) < Error(low));

        long Error(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new WebpCodec().OpenReader(stream);
            var decoded = new byte[pixels.Length];
            reader.Read(Region(width, height), decoded);
            return pixels.Zip(decoded, (a, b) => (long)(a - b) * (a - b)).Sum();
        }
    }

    [Fact]
    public void Lossless_RemainsPixelExact()
    {
        var pixels = Pixels(17, 19, true);
        var encoded = Encode(17, 19, pixels, new() { Lossless = true });
        using var stream = new MemoryStream(encoded);
        using var reader = new WebpCodec().OpenReader(stream);
        var decoded = new byte[pixels.Length];
        reader.Read(Region(17, 19), decoded);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void BoolEncoder_RoundTripsExtremeProbabilitiesAndCarries()
    {
        var random = new Random(743);
        var probabilities = Enumerable.Range(0, 50000).Select(index => index % 3 == 0 ? (index & 1) * 255 : random.Next(256)).ToArray();
        var bits = probabilities.Select(_ => random.Next(2)).ToArray();
        using var encoder = new Vp8BoolEncoder(new WebpMemory(1024 * 1024));
        for (var i = 0; i < bits.Length; i++) {
            encoder.Put(bits[i], probabilities[i]);
        }
        encoder.Finish();
        var decoder = new Vp8BoolDecoder(encoder.Bytes.ToArray());
        for (var i = 0; i < bits.Length; i++) {
            Assert.Equal(bits[i], decoder.GetBool(probabilities[i]));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidQuality_IsRejectedBeforeWriting(int quality)
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebpCodec().CreateWriter(stream, PngFixtures.Rgba8(), new() { Lossless = false, Quality = quality }));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void WorkingSetLimit_IsEnforcedDuringEncoding()
    {
        using var stream = new MemoryStream();
        using var writer = new WebpCodec().CreateWriter(stream, PngFixtures.Rgba8(16, 16), new() { Lossless = false, MaxWorkingSet = 2048 });
        writer.Write(Region(16, 16), Pixels(16, 16, false));
        var exception = Assert.Throws<ImageFormatException>(() => writer.Finish());
        Assert.Contains("working memory", exception.Message);
        Assert.Equal(0, stream.Length);
        Assert.Throws<InvalidOperationException>(() => writer.Finish());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encoding_PreservesMetadataAndAcceptsNonSeekableOutput(bool lossless)
    {
        var original = PngFixtures.Rgba8(17, 19);
        var part = original.Parts[0];
        byte[] icc = [1, 2, 3];
        byte[] exif = [4, 5, 6, 7];
        byte[] xmp = [8, 9, 10];
        var asset = original with { Parts = [part with {
            Color = part.Color! with { IccProfile = icc },
            Metadata = new MetadataCollection { Entries = [
                new MetadataEntry { Namespace = "webp", Name = "EXIF", RawRepresentation = exif },
                new MetadataEntry { Namespace = "webp", Name = "XMP", RawRepresentation = xmp },
            ] },
        }] };
        using var stream = new NonSeekableOutput();
        using (var writer = new WebpCodec().CreateWriter(stream, asset, new() { Lossless = lossless })) {
            writer.Write(Region(17, 19), Pixels(17, 19, true));
            writer.Finish();
            writer.Finish();
        }
        using var input = new MemoryStream(stream.ToArray());
        using var reader = new WebpCodec().OpenReader(input);
        var decoded = reader.Describe().Parts[0];
        Assert.Equal(icc, decoded.Color!.IccProfile);
        Assert.Equal(exif, decoded.Metadata.Entries.Single(entry => entry.Name == "EXIF").RawRepresentation);
        Assert.Equal(xmp, decoded.Metadata.Entries.Single(entry => entry.Name == "XMP").RawRepresentation);
    }

    private static byte[] Encode(int width, int height, byte[] pixels, WebpEncoderOptions options)
    {
        using var stream = new MemoryStream();
        using var writer = new WebpCodec().CreateWriter(stream, PngFixtures.Rgba8(width, height), options);
        writer.Write(Region(width, height), pixels);
        writer.Finish();
        return stream.ToArray();
    }

    private static WorkRegion Region(int width, int height) => new() {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height),
    };

    private static byte[] Pixels(int width, int height, bool alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var index = ((y * width) + x) * 4;
                pixels[index] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[index + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[index + 2] = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
                pixels[index + 3] = alpha ? (byte)((x * 17) + (y * 13)) : (byte)255;
            }
        }
        return pixels;
    }

    private sealed class NonSeekableOutput : MemoryStream
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
