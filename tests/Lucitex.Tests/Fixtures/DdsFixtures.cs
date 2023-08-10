using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Tests.Fixtures;

public static class DdsFixtures
{
    private static SpatialDomain Window(long width, long height) => new() {
        DataWindow = ImageBox.FromOrigin(width, height),
        DisplayWindow = ImageBox.FromOrigin(width, height),
    };

    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };

    private static IReadOnlyList<ResolutionLevel> MipChain(long width, long height, long depth = 1)
    {
        var levels = new List<ResolutionLevel>();
        var level = 0;
        long w = width, h = height, d = depth;

        while (true) {
            levels.Add(new ResolutionLevel { Key = LevelKey.Mip(level), Extent = new Extent3L(w, h, d) });
            if (w == 1 && h == 1 && d == 1)
                break;

            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            d = Math.Max(1, d / 2);
            level++;
        }

        return levels;
    }

    public static ImageAssetDescriptor Rgba8()
    {
        const long width = 64, height = 64;

        var part = new ImagePartDescriptor {
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.UNorm8),
                    Channel("G", SampleType.UNorm8),
                    Channel("B", SampleType.UNorm8),
                    Channel("A", SampleType.UNorm8),
                ],
            },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B", "A"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor R10G10B10A2()
    {
        const long width = 32, height = 32;

        var part = new ImagePartDescriptor {
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.UNorm16),
                    Channel("G", SampleType.UNorm16),
                    Channel("B", SampleType.UNorm16),
                    Channel("A", SampleType.UNorm8),
                ],
            },
            Representation = new EncodedElementRepresentation {
                Format = EncodedFormatId.R10G10B10A2,
                TexelExtentPerElement = new Extent3I(1, 1, 1),
                BitsPerElement = 32,
                Class = EncodedElementClass.Packed,
                PackedLayout = new PackedFieldLayout {
                    Fields =
                    [
                        new PackedField("R", 0, 10),
                        new PackedField("G", 10, 10),
                        new PackedField("B", 20, 10),
                        new PackedField("A", 30, 2),
                    ],
                },
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Bc7()
    {
        const long width = 128, height = 128;

        var part = new ImagePartDescriptor {
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.UNorm8),
                    Channel("G", SampleType.UNorm8),
                    Channel("B", SampleType.UNorm8),
                    Channel("A", SampleType.UNorm8),
                ],
            },
            Representation = new EncodedElementRepresentation {
                Format = EncodedFormatId.Bc7,
                TexelExtentPerElement = new Extent3I(4, 4, 1),
                BitsPerElement = 128,
                Class = EncodedElementClass.BlockCompressed,
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Cubemap()
    {
        const long size = 64;

        var part = new ImagePartDescriptor {
            Name = "cubemap",
            Spatial = Window(size, size),
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(size, size, 1),
                FaceCount = 6,
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(size, size, 1) }],
            },
            Channels = new ChannelSchema {
                Channels = [Channel("R", SampleType.UNorm8), Channel("G", SampleType.UNorm8), Channel("B", SampleType.UNorm8), Channel("A", SampleType.UNorm8)],
            },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B", "A"], Extent = new Extent3L(size, size, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Array()
    {
        const long width = 32, height = 32;

        var part = new ImagePartDescriptor {
            Name = "array",
            Spatial = Window(width, height),
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                ArrayElementCount = 8,
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Volume3D()
    {
        const long width = 16, height = 16, depth = 8;

        var part = new ImagePartDescriptor {
            Name = "volume",
            Spatial = Window(width, height),
            Topology = new ResourceTopology {
                SpatialDimensions = 3,
                BaseExtent = new Extent3L(width, height, depth),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, depth) }],
            },
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, depth) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor MipmappedTexture()
    {
        const long width = 64, height = 64;

        var part = new ImagePartDescriptor {
            Name = "texture",
            Spatial = Window(width, height),
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = MipChain(width, height),
            },
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.UNorm8)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }
}
