using Lucitex.Core.Color;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Jpeg.Decoding;

namespace Lucitex.Jpeg;

internal static class JpegDescriptorMapper
{
    private const string k_MetadataNamespace = "jpeg";

    public static ImageAssetDescriptor ToImageAssetDescriptor(JpegDecoder decoder)
    {
        var frame = decoder.Frame;
        var window = ImageBox.FromOrigin(frame.Width, frame.Height);

        var spatial = new SpatialDomain {
            DataWindow = window,
            DisplayWindow = window,
            Orientation = LogicalOrientation.Identity,
            Traversal = StorageTraversal.IncreasingY,
        };

        var topology = new ResourceTopology {
            SpatialDimensions = 2,
            BaseExtent = new Extent3L(frame.Width, frame.Height, 1),
            Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(frame.Width, frame.Height, 1) }],
        };

        var names = frame.Components.Count == 1 ? new[] { "Y" } : ["R", "G", "B"];
        var channels = names.Select(n => Channel(n, SampleType.UNorm8)).ToList();
        var plane = new SamplePlaneDescriptor {
            Channels = names.Select(n => (ChannelPath)n).ToList(),
            Extent = new Extent3L(frame.Width, frame.Height, 1),
            Layout = PlaneLayout.Interleaved,
        };

        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = spatial,
            Topology = topology,
            Channels = new ChannelSchema { Channels = channels },
            Representation = new PlainSampleRepresentation { Planes = [plane] },
            Color = new ColorEncoding { Transfer = TransferFunction.Srgb },
            Metadata = BuildMetadata(decoder),
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static MetadataCollection BuildMetadata(JpegDecoder decoder)
    {
        var entries = new List<MetadataEntry> {
            new() { Namespace = k_MetadataNamespace, Name = "Progressive", TypedValue = new Int64MetadataValue(decoder.Frame.Progressive ? 1 : 0) },
        };

        foreach (var comment in decoder.Comments) {
            entries.Add(new MetadataEntry { Namespace = k_MetadataNamespace, Name = "COM", TypedValue = new StringMetadataValue(comment) });
        }

        return new MetadataCollection { Entries = entries };
    }

    public static (int ComponentCount, int Width, int Height) ToJpegEncodeParams(ImagePartDescriptor part)
    {
        var width = (int)part.Topology.BaseExtent.Width;
        var height = (int)part.Topology.BaseExtent.Height;
        var componentCount = part.Channels.Channels.Count;

        if (componentCount is not (1 or 3)) {
            throw new NotSupportedException($"JPEG only supports 1 or 3 channel images, got {componentCount}.");
        }

        return (componentCount, width, height);
    }

    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };
}
