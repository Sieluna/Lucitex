using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Tests.Fixtures;

public static class HdrFixtures
{
    public static ImageAssetDescriptor Rgbe()
    {
        const long width = 32, height = 32;

        var window = ImageBox.FromOrigin(width, height);

        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels =
                [
                    new ChannelDescriptor { Name = "R", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
                    new ChannelDescriptor { Name = "G", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
                    new ChannelDescriptor { Name = "B", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
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
}
