using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Tests.Fixtures;

public static class Ktx2Fixtures
{
    private static SpatialDomain Window(long width, long height, LogicalOrientation orientation = default) => new()
    {
        DataWindow = ImageBox.FromOrigin(width, height),
        DisplayWindow = ImageBox.FromOrigin(width, height),
        Orientation = orientation == default ? LogicalOrientation.Identity : orientation,
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
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology
            {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
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
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Bc7()
    {
        const long width = 64, height = 64;

        var part = new ImagePartDescriptor
        {
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology
            {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema
            {
                Channels = [Channel("R", SampleType.UNorm8), Channel("G", SampleType.UNorm8), Channel("B", SampleType.UNorm8), Channel("A", SampleType.UNorm8)],
            },
            Representation = new EncodedElementRepresentation
            {
                Format = EncodedFormatId.Bc7,
                TexelExtentPerElement = new Extent3I(4, 4, 1),
                BitsPerElement = 128,
                Class = EncodedElementClass.BlockCompressed,
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor CubemapArray()
    {
        const long size = 32;

        var part = new ImagePartDescriptor
        {
            Name = "cubemapArray",
            Spatial = Window(size, size),
            Topology = new ResourceTopology
            {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(size, size, 1),
                FaceCount = 6,
                ArrayElementCount = 4,
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(size, size, 1) }],
            },
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation
            {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(size, size, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Oriented()
    {
        const long width = 16, height = 16;

        var part = new ImagePartDescriptor
        {
            Name = "texture",
            Spatial = Window(width, height, LogicalOrientation.FlipY),
            Topology = new ResourceTopology
            {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation
            {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor ZlibSupercompressed()
    {
        const long width = 64, height = 64;

        var part = new ImagePartDescriptor
        {
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology
            {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels =
                [
                    new ResolutionLevel
                    {
                        Key = LevelKey.Base,
                        Extent = new Extent3L(width, height, 1),
                        Supercompression = new SupercompressionDescriptor
                        {
                            Scheme = SupercompressionScheme.Zlib,
                            UncompressedByteLength = width * height * 4,
                        },
                    },
                ],
            },
            Channels = new ChannelSchema
            {
                Channels = [Channel("R", SampleType.UNorm8), Channel("G", SampleType.UNorm8), Channel("B", SampleType.UNorm8), Channel("A", SampleType.UNorm8)],
            },
            Representation = new PlainSampleRepresentation
            {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B", "A"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }
}
