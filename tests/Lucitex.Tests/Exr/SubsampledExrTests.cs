using Lucitex.Core.Execution;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Exr;

public class SubsampledExrTests
{
    private static ImageAssetDescriptor LuminanceChroma(long width, long height, long minX, long minY, int chromaStep)
    {
        var window = ImageBox.FromExclusive(minX, minY, minX + width, minY + height);
        var step = new Int3(chromaStep, chromaStep, 1);

        ChannelDescriptor Channel(string name, Int3 sampling) => new() {
            Name = name,
            SampleType = SampleType.Float16,
            Sampling = new SampleGrid { Origin = Long3.Zero, Step = sampling },
        };

        long Samples(long size, int sampling) => (size + sampling - 1) / sampling;

        var part = new ImagePartDescriptor {
            Name = "yc",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
            Topology = new ResourceTopology {
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels = [Channel("Y", Int3.One), Channel("RY", step), Channel("BY", step)],
            },
            Representation = new PlainSampleRepresentation {
                Planes = [
                    new SamplePlaneDescriptor { Channels = ["Y"], Extent = new Extent3L(width, height, 1) },
                    new SamplePlaneDescriptor {
                        Channels = ["RY"],
                        Extent = new Extent3L(Samples(width, chromaStep), Samples(height, chromaStep), 1),
                    },
                    new SamplePlaneDescriptor {
                        Channels = ["BY"],
                        Extent = new Extent3L(Samples(width, chromaStep), Samples(height, chromaStep), 1),
                    },
                ],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static int ExpectedStoredBytes(ExrHeader header)
    {
        var total = 0;

        for (var y = header.DataWindow.YMin; y <= header.DataWindow.YMax; y++) {
            foreach (var channel in header.Channels) {
                if (y % channel.YSampling != 0) {
                    continue;
                }

                var columns = 0;
                for (var x = header.DataWindow.XMin; x <= header.DataWindow.XMax; x++) {
                    if (x % channel.XSampling == 0) {
                        columns++;
                    }
                }

                total += columns * channel.BytesPerSample;
            }
        }

        return total;
    }

    private static byte[] RoundTrip(ImageAssetDescriptor asset, ExrCompressionId compression, out ImageAssetDescriptor described, out byte[] source)
    {
        var window = asset.Parts[0].Spatial.DataWindow;
        var header = ExrDescriptorMapper.ToExrHeader(asset, compression);

        source = new byte[ExpectedStoredBytes(header)];
        new Random(2024).NextBytes(source);

        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = window };
        var codec = new ExrCodec(compression);
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        described = reader.Describe();

        var destination = new byte[source.Length];
        var readCount = reader.Read(region, destination);

        Assert.Equal(source.Length, readCount);
        return destination;
    }

    [Theory]
    [InlineData(ExrCompressionId.None)]
    [InlineData(ExrCompressionId.Rle)]
    [InlineData(ExrCompressionId.Zips)]
    [InlineData(ExrCompressionId.Zip)]
    [InlineData(ExrCompressionId.Piz)]
    public void RoundTrip_SubsampledChromaFixture_PreservesEveryStoredSample(ExrCompressionId compression)
    {
        var asset = ExrFixtures.SubsampledChannels();

        var destination = RoundTrip(asset, compression, out var described, out var source);

        Assert.Equal(source, destination);

        var channels = described.Parts[0].Channels.Channels.ToDictionary(c => c.Name.FullName);
        Assert.Equal(new Int3(1, 1, 1), channels["Y"].Sampling.Step);
        Assert.Equal(new Int3(2, 2, 1), channels["RY"].Sampling.Step);
        Assert.Equal(new Int3(2, 2, 1), channels["BY"].Sampling.Step);
    }

    [Theory]
    [InlineData(33, 17, 0, 0, 2)]
    [InlineData(33, 17, -3, -5, 2)]
    [InlineData(40, 40, 7, 11, 4)]
    [InlineData(19, 41, -8, -8, 2)]
    public void RoundTrip_SubsampledWindows_PreservesEveryStoredSample(long width, long height, long minX, long minY, int chromaStep)
    {
        var asset = LuminanceChroma(width, height, minX, minY, chromaStep);

        var destination = RoundTrip(asset, ExrCompressionId.Zip, out var described, out var source);

        Assert.Equal(source, destination);

        var planes = ((PlainSampleRepresentation)described.Parts[0].Representation!).Planes
            .ToDictionary(plane => plane.Channels[0].FullName);

        Assert.Equal(width, planes["Y"].Extent.Width);
        Assert.Equal(height, planes["Y"].Extent.Height);
        Assert.Equal((width + chromaStep - 1) / chromaStep, planes["RY"].Extent.Width);
        Assert.Equal((height + chromaStep - 1) / chromaStep, planes["RY"].Extent.Height);
    }

    [Fact]
    public void StoredSize_IsSmallerThanFullResolution()
    {
        var asset = ExrFixtures.SubsampledChannels();
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Zip);

        var stored = ExpectedStoredBytes(header);
        var full = header.Channels.Sum(c => (int)(header.DataWindow.Width * header.DataWindow.Height) * c.BytesPerSample);

        Assert.Equal(32 * 32 * 2 + (2 * 16 * 16 * 2), stored);
        Assert.True(stored < full);
    }

    [Fact]
    public void CreateWriter_RejectsSubsampledChannelsInTiledParts()
    {
        var asset = ExrFixtures.SubsampledChannels();
        var tiles = new ExrTileDesc(8, 8, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown);
        var codec = new ExrCodec(ExrCompressionId.Zip, tiles);
        using var stream = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => codec.CreateWriter(stream, asset));
    }
}
