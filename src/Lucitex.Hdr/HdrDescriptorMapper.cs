using Lucitex.Core.Color;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Hdr;

internal static class HdrDescriptorMapper
{
    public static ImageAssetDescriptor ToDescriptor(HdrHeader header)
    {
        var window = ImageBox.FromOrigin(header.Width, header.Height);
        var extent = new Extent3L(header.Width, header.Height, 1);
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain {
                DataWindow = window,
                DisplayWindow = window,
                Orientation = header.Orientation,
                Traversal = StorageTraversal.IncreasingY,
            },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = extent,
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = extent }],
            },
            Channels = new ChannelSchema {
                Channels =
                [
                    Channel("R", ChannelSemantic.Red),
                    Channel("G", ChannelSemantic.Green),
                    Channel("B", ChannelSemantic.Blue),
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
            Color = new ColorEncoding { Transfer = TransferFunction.Linear },
        };
        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static (int Width, int Height, LogicalOrientation Orientation) ValidateForWriting(ImageAssetDescriptor descriptor)
    {
        if (descriptor.Parts.Count != 1) {
            throw new NotSupportedException("HDR requires exactly one image part.");
        }

        var part = descriptor.Parts[0];
        if (part.Representation is not EncodedElementRepresentation { Format.Name: nameof(EncodedFormatId.Rgbe), BitsPerElement: 32 }) {
            throw new NotSupportedException("HDR writer requires 32-bit RGBE encoded elements.");
        }

        if (part.Topology.SpatialDimensions != 2 || part.Topology.ArrayElementCount != 1 || part.Topology.FaceCount != 1 || part.Topology.Levels.Count != 1) {
            throw new NotSupportedException("HDR writer requires one two-dimensional base image.");
        }

        var width = checked((int)part.Topology.BaseExtent.Width);
        var height = checked((int)part.Topology.BaseExtent.Height);
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "HDR dimensions must be positive.");
        }

        return (width, height, part.Spatial.Orientation);
    }

    private static ChannelDescriptor Channel(string name, ChannelSemantic semantic) => new() {
        Name = name,
        Semantic = semantic,
        SampleType = SampleType.Float32,
        Sampling = SampleGrid.Unit,
    };
}
