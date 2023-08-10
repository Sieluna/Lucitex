using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Tests.Fixtures;

public static class ExrFixtures
{
    private static SpatialDomain Window(long width, long height, long minX = 0, long minY = 0)
    {
        var window = ImageBox.FromExclusive(minX, minY, minX + width, minY + height);
        return new SpatialDomain {
            DataWindow = window,
            DisplayWindow = window,
        };
    }

    private static ResourceTopology FlatTopology(long width, long height) => new() {
        SpatialDimensions = 2,
        BaseExtent = new Extent3L(width, height, 1),
        Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
    };

    private static ChannelDescriptor Channel(string name, SampleType sampleType, Int3? step = null) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = new SampleGrid { Origin = Long3.Zero, Step = step ?? Int3.One },
    };

    public static ImageAssetDescriptor SimpleRgba()
    {
        const long width = 64, height = 32;

        var part = new ImagePartDescriptor {
            Name = "rgba",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.Float16),
                    Channel("G", SampleType.Float16),
                    Channel("B", SampleType.Float16),
                    Channel("A", SampleType.Float16),
                ],
            },
            Representation = new PlainSampleRepresentation {
                Planes =
                [
                    new SamplePlaneDescriptor
                    {
                        Channels = ["R", "G", "B", "A"],
                        Extent = new Extent3L(width, height, 1),
                        Layout = PlaneLayout.Interleaved,
                    },
                ],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor MixedHalfFloat()
    {
        const long width = 16, height = 16;

        var part = new ImagePartDescriptor {
            Name = "beauty",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.Float16),
                    Channel("G", SampleType.Float16),
                    Channel("B", SampleType.Float16),
                    Channel("Z", SampleType.Float32),
                    Channel("id", SampleType.UInt32),
                ],
            },
            Representation = new PlainSampleRepresentation {
                Planes =
                [
                    new SamplePlaneDescriptor { Channels = ["R", "G", "B"], Extent = new Extent3L(width, height, 1) },
                    new SamplePlaneDescriptor { Channels = ["Z"], Extent = new Extent3L(width, height, 1) },
                    new SamplePlaneDescriptor { Channels = ["id"], Extent = new Extent3L(width, height, 1) },
                ],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor SubsampledChannels()
    {
        const long width = 32, height = 32;

        var part = new ImagePartDescriptor {
            Name = "yc",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("Y", SampleType.Float16),
                    Channel("RY", SampleType.Float16, new Int3(2, 2, 1)),
                    Channel("BY", SampleType.Float16, new Int3(2, 2, 1)),
                ],
            },
            Representation = new PlainSampleRepresentation {
                Planes =
                [
                    new SamplePlaneDescriptor { Channels = ["Y"], Extent = new Extent3L(width, height, 1) },
                    new SamplePlaneDescriptor { Channels = ["RY"], Extent = new Extent3L(width / 2, height / 2, 1) },
                    new SamplePlaneDescriptor { Channels = ["BY"], Extent = new Extent3L(width / 2, height / 2, 1) },
                ],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor Multipart()
    {
        const long width = 8, height = 8;

        ImagePartDescriptor MakePart(string name, string channel) => new() {
            Name = name,
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema { Channels = [Channel(channel, SampleType.Float16)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = [channel], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor {
            Parts =
            [
                MakePart("diffuse", "diffuse.R"),
                MakePart("specular", "specular.R"),
            ],
        };
    }

    public static ImageAssetDescriptor Ripmap()
    {
        const long width = 4, height = 4;

        var topology = new ResourceTopology {
            SpatialDimensions = 2,
            BaseExtent = new Extent3L(width, height, 1),
            Levels =
            [
                new ResolutionLevel { Key = new LevelKey(0, 0, 0), Extent = new Extent3L(4, 4, 1) },
                new ResolutionLevel { Key = new LevelKey(1, 0, 0), Extent = new Extent3L(2, 4, 1) },
                new ResolutionLevel { Key = new LevelKey(0, 1, 0), Extent = new Extent3L(4, 2, 1) },
                new ResolutionLevel { Key = new LevelKey(1, 1, 0), Extent = new Extent3L(2, 2, 1) },
            ],
        };

        var part = new ImagePartDescriptor {
            Name = "ripmap",
            Spatial = Window(width, height),
            Topology = topology,
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.Float16)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor NegativeDataWindow()
    {
        var window = ImageBox.FromExclusive(-10, -5, 10, 5);
        const long width = 20, height = 10;

        var part = new ImagePartDescriptor {
            Name = "shifted",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema { Channels = [Channel("R", SampleType.Float32)] },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1) }],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static ImageAssetDescriptor DeepDescriptor()
    {
        const long width = 8, height = 8;

        var part = new ImagePartDescriptor {
            Name = "deep",
            Spatial = Window(width, height),
            Topology = FlatTopology(width, height),
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.Float32),
                    Channel("G", SampleType.Float32),
                    Channel("B", SampleType.Float32),
                    Channel("A", SampleType.Float32),
                ],
            },
            Representation = new DeepRepresentation {
                SampleChannels = new ChannelSchema {
                    Channels =
                    [
                        Channel("R", SampleType.Float32),
                        Channel("G", SampleType.Float32),
                        Channel("B", SampleType.Float32),
                        Channel("A", SampleType.Float32),
                    ],
                },
                OffsetType = SampleType.UInt32,
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }
}
