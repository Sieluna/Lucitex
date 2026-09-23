using Lucitex.Core.Execution;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Jpeg;
using Lucitex.Jpeg.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Jpeg;

public class JpegCodecEndToEndTests
{
    private static WorkRegion FullRegion(long width, long height) => new() {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    private static byte[] GradientRgb(long width, long height)
    {
        var buffer = new byte[width * height * 3];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var offset = ((y * width) + x) * 3;
                buffer[offset + 0] = (byte)((x * 255) / Math.Max(1, width - 1));
                buffer[offset + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                buffer[offset + 2] = (byte)(((x + y) * 255) / Math.Max(1, width + height - 2));
            }
        }

        return buffer;
    }

    private static byte[] GradientGray(long width, long height)
    {
        var buffer = new byte[width * height];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                buffer[(y * width) + x] = (byte)(((x + y) * 255) / Math.Max(1, width + height - 2));
            }
        }

        return buffer;
    }

    private static byte[] NoiseRgb(long width, long height, int seed = 7)
    {
        var buffer = new byte[width * height * 3];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    private static byte[] NoiseGray(long width, long height, int seed = 7)
    {
        var buffer = new byte[width * height];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    private static byte[] RoundTrip(ImageAssetDescriptor asset, byte[] source) => RoundTrip(asset, source, out _);

    private static byte[] RoundTrip(ImageAssetDescriptor asset, byte[] source, out byte[] encoded, bool progressive = false)
    {
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var codec = new JpegCodec();
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset, new JpegEncoderOptions { Progressive = progressive });
        writer.Write(FullRegion(width, height), source);
        writer.Finish();

        encoded = stream.ToArray();

        stream.Position = 0;
        using var reader = codec.OpenReader(stream);

        Assert.Equal(width, reader.Describe().Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(height, reader.Describe().Parts[0].Topology.BaseExtent.Height);

        var destination = new byte[source.Length];
        var readCount = reader.Read(FullRegion(width, height), destination);

        Assert.Equal(source.Length, readCount);
        return destination;
    }

    private static bool ContainsMarker(byte[] data, byte marker)
    {
        for (var i = 0; i < data.Length - 1; i++) {
            if (data[i] == 0xFF && data[i + 1] == marker) {
                return true;
            }
        }

        return false;
    }

    private static void AssertCloseEnough(byte[] source, byte[] destination, double maxMeanAbsoluteError)
    {
        Assert.Equal(source.Length, destination.Length);

        double sum = 0;
        for (var i = 0; i < source.Length; i++) {
            sum += Math.Abs(source[i] - destination[i]);
        }

        var meanAbsoluteError = sum / source.Length;
        Assert.True(meanAbsoluteError <= maxMeanAbsoluteError, $"Mean absolute error {meanAbsoluteError} exceeded {maxMeanAbsoluteError}.");
    }

    [Fact]
    public void RoundTrip_Rgb8_StaysCloseToOriginal()
    {
        var asset = JpegFixtures.Rgb8();
        var source = GradientRgb(asset.Parts[0].Topology.BaseExtent.Width, asset.Parts[0].Topology.BaseExtent.Height);

        var destination = RoundTrip(asset, source);

        AssertCloseEnough(source, destination, 8.0);
    }

    [Fact]
    public void RoundTrip_Grayscale8_StaysCloseToOriginal()
    {
        var asset = JpegFixtures.Grayscale8();
        var source = GradientGray(asset.Parts[0].Topology.BaseExtent.Width, asset.Parts[0].Topology.BaseExtent.Height);

        var destination = RoundTrip(asset, source);

        AssertCloseEnough(source, destination, 6.0);
    }

    [Fact]
    public void RoundTrip_Rgb8_LargeEnoughForRestartIntervals_StaysCloseToOriginal()
    {
        var asset = JpegFixtures.Rgb8(512, 512);
        var source = NoiseRgb(512, 512);

        var destination = RoundTrip(asset, source, out var encoded);

        AssertCloseEnough(source, destination, 50.0);
        Assert.True(ContainsMarker(encoded, JpegMarkers.Dri), "Expected a DRI marker for an image with enough AC coefficient energy to split into parallel restart intervals.");
        Assert.True(ContainsMarker(encoded, JpegMarkers.Rst0), "Expected at least one RST0 marker between restart-interval segments.");
    }

    [Fact]
    public void RoundTrip_Grayscale8_LargeEnoughForRestartIntervals_StaysCloseToOriginal()
    {
        var asset = JpegFixtures.Grayscale8(512, 512);
        var source = NoiseGray(512, 512);

        var destination = RoundTrip(asset, source, out var encoded);

        AssertCloseEnough(source, destination, 50.0);
        Assert.True(ContainsMarker(encoded, JpegMarkers.Dri), "Expected a DRI marker for an image with enough AC coefficient energy to split into parallel restart intervals.");
    }

    [Fact]
    public void RoundTrip_NonMcuAlignedDimensions_PreservesExactSize()
    {
        var asset = JpegFixtures.Rgb8(37, 23);
        var source = GradientRgb(37, 23);

        var destination = RoundTrip(asset, source);

        AssertCloseEnough(source, destination, 10.0);
    }

    [Fact]
    public void RoundTrip_Rgb8_Progressive_StaysCloseToOriginal()
    {
        var asset = JpegFixtures.Rgb8();
        var source = GradientRgb(asset.Parts[0].Topology.BaseExtent.Width, asset.Parts[0].Topology.BaseExtent.Height);

        var destination = RoundTrip(asset, source, out var encoded, progressive: true);

        AssertCloseEnough(source, destination, 8.0);
        Assert.True(ContainsMarker(encoded, 0xC2));
    }

    [Fact]
    public void RoundTrip_Grayscale8_Progressive_StaysCloseToOriginal()
    {
        var asset = JpegFixtures.Grayscale8();
        var source = GradientGray(asset.Parts[0].Topology.BaseExtent.Width, asset.Parts[0].Topology.BaseExtent.Height);

        var destination = RoundTrip(asset, source, out var encoded, progressive: true);

        AssertCloseEnough(source, destination, 6.0);
        Assert.True(ContainsMarker(encoded, 0xC2));
    }

    [Fact]
    public void RoundTrip_Progressive_NonMcuAlignedDimensions_PreservesExactSize()
    {
        var asset = JpegFixtures.Rgb8(37, 23);
        var source = GradientRgb(37, 23);

        var destination = RoundTrip(asset, source, out _, progressive: true);

        AssertCloseEnough(source, destination, 10.0);
    }

    [Fact]
    public void Probe_RecognizesJpegSignature()
    {
        var codec = new JpegCodec();
        var result = codec.Probe([0xFF, 0xD8, 0xFF, 0xE0]);

        Assert.Equal(Lucitex.Core.Execution.Codecs.ProbeConfidence.Certain, result.Confidence);
        Assert.Equal("jpeg", result.Format);
    }

    [Fact]
    public void CodecRegistry_ResolvesJpegBySignature()
    {
        var registry = new Lucitex.Core.Execution.Codecs.CodecRegistry();
        registry.Register(new JpegCodec());

        var resolved = registry.Resolve([0xFF, 0xD8, 0xFF, 0xE0]);

        Assert.Equal("jpeg", resolved.FormatId);
    }
}
