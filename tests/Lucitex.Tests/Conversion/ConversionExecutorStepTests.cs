using Lucitex.Conversion;
using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Tests.RawCodec;

namespace Lucitex.Tests.Conversion;

public class ConversionExecutorStepTests
{
    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };

    private static ImageAssetDescriptor BuildRgbaDescriptor(long width, long height, SampleType sampleType, LogicalOrientation orientation, AlphaDescriptor? alpha)
    {
        var window = ImageBox.FromOrigin(width, height);
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window, Orientation = orientation },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = [Channel("R", sampleType), Channel("G", sampleType), Channel("B", sampleType), Channel("A", sampleType)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B", "A"], Extent = new Extent3L(width, height, 1), Layout = PlaneLayout.Interleaved }],
            },
            Alpha = alpha,
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    [Fact]
    public void Execute_NonIdentityOrientation_ReorientsPixelsToIdentity()
    {
        var descriptor = BuildRgbaDescriptor(2, 2, SampleType.UNorm8, LogicalOrientation.FlipY, alpha: null);
        var codec = new RawTestCodec();

        // Row0: (10,10,10,10) (20,20,20,20); Row1: (30,30,30,30) (40,40,40,40)
        byte[] source =
        [
            10, 10, 10, 10, 20, 20, 20, 20,
            30, 30, 30, 30, 40, 40, 40, 40,
        ];

        using var sourceStream = new MemoryStream();
        var sourceWriter = codec.CreateWriter(sourceStream, descriptor);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(2, 2) };
        sourceWriter.Write(region, source);
        sourceWriter.Finish();

        sourceStream.Position = 0;
        var reader = codec.OpenReader(sourceStream);

        var planResult = ConversionPlanner.Plan(reader.Describe(), codec.Capabilities, ConversionPolicy.Preserve);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, s => s is ApplyOrientationStep);

        using var targetStream = new MemoryStream();
        var writer = codec.CreateWriter(targetStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, reader, codec.Capabilities.SampleByteOrder, writer, codec.Capabilities.SampleByteOrder);

        targetStream.Position = 0;
        var targetReader = codec.OpenReader(targetStream);
        var targetDescribed = targetReader.Describe();
        Assert.Equal(2, (int)targetDescribed.Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(2, (int)targetDescribed.Parts[0].Topology.BaseExtent.Height);

        var destination = new byte[source.Length];
        targetReader.Read(region, destination);

        // FlipY reverses row order: row0 becomes the original row1, and vice versa.
        byte[] expected =
        [
            30, 30, 30, 30, 40, 40, 40, 40,
            10, 10, 10, 10, 20, 20, 20, 20,
        ];
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void Execute_PremultipliedAlphaSource_UnpremultipliesColorChannels()
    {
        var descriptor = BuildRgbaDescriptor(1, 1, SampleType.Float32, LogicalOrientation.Identity, new AlphaDescriptor { Mode = AlphaMode.Premultiplied });
        var codec = new RawTestCodec();

        var premultiplied = new byte[4 * 4];
        float[] premultipliedValues = [0.32f, 0.32f, 0.32f, 0.4f];
        for (var i = 0; i < 4; i++) {
            BitConverter.GetBytes(premultipliedValues[i]).CopyTo(premultiplied, i * 4);
        }

        using var sourceStream = new MemoryStream();
        var sourceWriter = codec.CreateWriter(sourceStream, descriptor);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(1, 1) };
        sourceWriter.Write(region, premultiplied);
        sourceWriter.Finish();

        sourceStream.Position = 0;
        var reader = codec.OpenReader(sourceStream);

        var planResult = ConversionPlanner.Plan(reader.Describe(), codec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, s => s is UnpremultiplyAlphaStep);

        using var targetStream = new MemoryStream();
        var writer = codec.CreateWriter(targetStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, reader, codec.Capabilities.SampleByteOrder, writer, codec.Capabilities.SampleByteOrder);

        targetStream.Position = 0;
        var targetReader = codec.OpenReader(targetStream);
        var destination = new byte[16];
        targetReader.Read(region, destination);

        var straightR = BitConverter.ToSingle(destination, 0);
        var straightG = BitConverter.ToSingle(destination, 4);
        var straightB = BitConverter.ToSingle(destination, 8);
        var alpha = BitConverter.ToSingle(destination, 12);

        Assert.Equal(0.8f, straightR, 0.0001f);
        Assert.Equal(0.8f, straightG, 0.0001f);
        Assert.Equal(0.8f, straightB, 0.0001f);
        Assert.Equal(0.4f, alpha, 0.0001f);
    }
}
