using Lucitex.Core.Color;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Benchmarks.Data;

internal static class BenchmarkDescriptors
{
    public static WorkRegionFactory Region { get; } = new();

    public static ImageAssetDescriptor Png(long width, long height, SampleType sampleType, string[] channels) =>
        Plain(width, height, sampleType, channels, new ColorEncoding { Transfer = TransferFunction.Srgb });

    public static ImageAssetDescriptor Exr(long width, long height, SampleType sampleType, string[] channels) =>
        Plain(width, height, sampleType, channels, new ColorEncoding { Transfer = TransferFunction.Linear });

    public static ImageAssetDescriptor Hdr(long width, long height)
    {
        var part = Part(width, height, new ColorEncoding { Transfer = TransferFunction.Linear }) with {
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", SampleType.Float32),
                    Channel("G", SampleType.Float32),
                    Channel("B", SampleType.Float32),
                ],
            },
            Representation = new EncodedElementRepresentation {
                Format = EncodedFormatId.Rgbe,
                TexelExtentPerElement = new Extent3I(1, 1, 1),
                BitsPerElement = 32,
                Class = EncodedElementClass.SharedExponent,
                PackedLayout = new PackedFieldLayout {
                    Fields =
                    [
                        new PackedField("R", 0, 8),
                        new PackedField("G", 8, 8),
                        new PackedField("B", 16, 8),
                        new PackedField("E", 24, 8),
                    ],
                },
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static ImageAssetDescriptor Plain(long width, long height, SampleType sampleType, string[] channels, ColorEncoding color)
    {
        var part = Part(width, height, color) with {
            Channels = new ChannelSchema { Channels = [.. channels.Select(name => Channel(name, sampleType))] },
            Representation = new PlainSampleRepresentation {
                Planes =
                [
                    new SamplePlaneDescriptor {
                        Channels = [.. channels.Select(name => (ChannelPath)name)],
                        Extent = new Extent3L(width, height, 1),
                        Layout = PlaneLayout.Interleaved,
                    },
                ],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static ImagePartDescriptor Part(long width, long height, ColorEncoding color)
    {
        var window = ImageBox.FromOrigin(width, height);
        return new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = [] },
            Representation = new PlainSampleRepresentation { Planes = [] },
            Color = color,
        };
    }

    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };

    internal sealed class WorkRegionFactory
    {
        public Lucitex.Core.Execution.WorkRegion Full(long width, long height) => new() {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
    }
}
