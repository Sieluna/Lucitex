using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Benchmarks.Data;

internal static class ImageLayout
{
    public static ImageAssetDescriptor Describe(TestImage source, bool half = false)
    {
        string[] channels = source.Channels == 4 ? ["R", "G", "B", "A"] : ["R", "G", "B"];
        var extent = new Extent3L(source.Width, source.Height, 1);
        var window = ImageBox.FromOrigin(source.Width, source.Height);
        return new ImageAssetDescriptor {
            Parts = [new ImagePartDescriptor {
                Name = "image",
                Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
                Topology = new ResourceTopology {
                    SpatialDimensions = 2, BaseExtent = extent,
                    Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = extent }],
                },
                Channels = new ChannelSchema {
                    Channels = [.. channels.Select(name => new ChannelDescriptor { Name = name, SampleType = half ? SampleType.Float16 : SampleType.UNorm8, Sampling = SampleGrid.Unit })],
                },
                Representation = new PlainSampleRepresentation {
                    Planes = [new SamplePlaneDescriptor { Channels = [.. channels.Select(name => (ChannelPath)name)], Extent = extent, Layout = PlaneLayout.Interleaved }],
                },
                Color = new ColorEncoding { Transfer = half ? TransferFunction.Linear : TransferFunction.Srgb },
            }],
        };
    }

    public static WorkRegion Full(TestImage source) => new() {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(source.Width, source.Height),
    };
}
