using Lucitex.Core.Color;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Tests.Fixtures;

public static class PngFixtures
{
    private static SpatialDomain Window(long width, long height) => new()
    {
        DataWindow = ImageBox.FromOrigin(width, height),
        DisplayWindow = ImageBox.FromOrigin(width, height),
    };

    private static ResourceTopology FlatTopology(long width, long height) => new()
    {
        SpatialDimensions = 2,
        BaseExtent = new Extent3L(width, height, 1),
        Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
    };

    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new()
    {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };

    public static ImageAssetDescriptor Rgba8()
    {
        const long width = 32, height = 32;

        var part = new ImagePartDescriptor
        {
            Name = "image",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema
            {
                Channels =
                [
                    Channel("R", SampleType.UNorm8),
                    Channel("G", SampleType.UNorm8),
                    Channel("B", SampleType.UNorm8),
                    Channel("A", SampleType.UNorm8),
                ],
            },
            Representation = new PlainSampleRepresentation
            {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B", "A"], Extent = new Extent3L(width, height, 1) }],
            },
            Color = new ColorEncoding { Transfer = TransferFunction.Srgb },
            Alpha = new AlphaDescriptor { Mode = AlphaMode.Straight },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Rgb16()
    {
        const long width = 16, height = 16;

        var part = new ImagePartDescriptor
        {
            Name = "image",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema
            {
                Channels =
                [
                    Channel("R", SampleType.UNorm16),
                    Channel("G", SampleType.UNorm16),
                    Channel("B", SampleType.UNorm16),
                ],
            },
            Representation = new PlainSampleRepresentation
            {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B"], Extent = new Extent3L(width, height, 1) }],
            },
            Color = new ColorEncoding { Transfer = TransferFunction.Linear },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Grayscale1Bit()
    {
        const long width = 24, height = 8;

        var part = new ImagePartDescriptor
        {
            Name = "image",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema { Channels = [Channel("Y", SampleType.UNorm1)] },
            Representation = new PlainSampleRepresentation
            {
                Planes = [new SamplePlaneDescriptor { Channels = ["Y"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Palette4Bit()
    {
        const long width = 16, height = 16;

        var part = new ImagePartDescriptor
        {
            Name = "image",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema { Channels = [Channel("Index", SampleType.UNorm4)] },
            Representation = new IndexedRepresentation
            {
                IndexType = SampleType.UNorm4,
                Palette = new PaletteDescriptor
                {
                    EntryCount = 16,
                    EntryChannels = new ChannelSchema
                    {
                        Channels =
                        [
                            Channel("R", SampleType.UNorm8),
                            Channel("G", SampleType.UNorm8),
                            Channel("B", SampleType.UNorm8),
                        ],
                    },
                    EntrySampleType = SampleType.UNorm8,
                },
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }
}
