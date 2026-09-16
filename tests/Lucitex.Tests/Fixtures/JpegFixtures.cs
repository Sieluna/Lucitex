using Lucitex.Core.Color;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Tests.Fixtures;

public static class JpegFixtures
{
    private static SpatialDomain Window(long width, long height) => new() {
        DataWindow = ImageBox.FromOrigin(width, height),
        DisplayWindow = ImageBox.FromOrigin(width, height),
    };

    private static ResourceTopology FlatTopology(long width, long height) => new() {
        SpatialDimensions = 2,
        BaseExtent = new Extent3L(width, height, 1),
        Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
    };

    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };

    public static ImageAssetDescriptor Rgb8(long width = 48, long height = 32)
    {
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.UNorm8), Channel("G", SampleType.UNorm8), Channel("B", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B"], Extent = new Extent3L(width, height, 1) }],
            },
            Color = new ColorEncoding { Transfer = TransferFunction.Srgb },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Grayscale8(long width = 40, long height = 24)
    {
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema { Channels = [Channel("Y", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["Y"], Extent = new Extent3L(width, height, 1) }],
            },
            Color = new ColorEncoding { Transfer = TransferFunction.Srgb },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }
}
