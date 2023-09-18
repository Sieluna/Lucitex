using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Hdr;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Hdr;

public class HdrCodecEndToEndTests
{
    [Theory]
    [InlineData(32, 19)]
    [InlineData(4, 7)]
    public void RoundTrip_PreservesRgbeElements(int width, int height)
    {
        var descriptor = WithSize(HdrFixtures.Rgbe(), width, height);
        var pixels = new byte[width * height * 4];
        new Random(42).NextBytes(pixels);

        var codec = new HdrCodec();
        using var stream = new MemoryStream();
        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
        var writer = codec.CreateWriter(stream, descriptor);
        writer.Write(region, pixels);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var destination = new byte[pixels.Length];
        Assert.Equal(pixels.Length, reader.Read(region, destination));
        Assert.Equal(pixels, destination);
        Assert.Equal(width, reader.Describe().Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(height, reader.Describe().Parts[0].Topology.BaseExtent.Height);
    }

    [Fact]
    public void Reader_InvalidRlePacket_ReportsImageFormatException()
    {
        var descriptor = WithSize(HdrFixtures.Rgbe(), 32, 1);
        var codec = new HdrCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, descriptor);
        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(32, 1),
        };
        writer.Write(region, new byte[32 * 4]);
        writer.Finish();

        var bytes = stream.ToArray();
        var marker = Find(bytes, [2, 2, 0, 32]);
        bytes[marker + 4] = 0;
        var reader = codec.OpenReader(new MemoryStream(bytes));

        var exception = Assert.Throws<ImageFormatException>(() => reader.Read(region, new byte[32 * 4]));

        Assert.Equal("hdr", exception.Format);
    }

    [Fact]
    public void Probe_RecognizesBothSignatures()
    {
        var codec = new HdrCodec();

        Assert.Equal(ProbeConfidence.Certain, codec.Probe("#?RADIANCE"u8).Confidence);
        Assert.Equal(ProbeConfidence.Certain, codec.Probe("#?RGBE"u8).Confidence);
    }

    private static int Find(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value)
    {
        var index = data.IndexOf(value);
        Assert.True(index >= 0);
        return index;
    }

    private static ImageAssetDescriptor WithSize(ImageAssetDescriptor descriptor, int width, int height)
    {
        var part = descriptor.Parts[0];
        var extent = new Extent3L(width, height, 1);
        var window = ImageBox.FromOrigin(width, height);
        return descriptor with {
            Parts =
            [
                part with {
                    Spatial = part.Spatial with { DataWindow = window, DisplayWindow = window },
                    Topology = part.Topology with {
                        BaseExtent = extent,
                        Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = extent }],
                    },
                },
            ],
        };
    }
}
