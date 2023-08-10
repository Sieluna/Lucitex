using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Dds.Format;

namespace Lucitex.Dds;

internal static class DdsDescriptorMapper
{
    public static ImageAssetDescriptor ToImageAssetDescriptor(DdsHeader header)
    {
        var window = ImageBox.FromOrigin(header.Width, header.Height);

        var spatial = new SpatialDomain {
            DataWindow = window,
            DisplayWindow = window,
            Orientation = LogicalOrientation.Identity,
            Traversal = StorageTraversal.IncreasingY,
        };

        var spatialDimensions = header.Dimension == D3d10ResourceDimension.Texture3D ? 3 : 2;
        var baseExtent = new Extent3L(header.Width, header.Height, header.Dimension == D3d10ResourceDimension.Texture3D ? header.Depth : 1);

        var levels = new List<ResolutionLevel>();
        var w = (long)header.Width;
        var h = (long)header.Height;
        var d = spatialDimensions == 3 ? (long)header.Depth : 1;
        for (var mip = 0; mip < header.MipMapCount; mip++) {
            levels.Add(new ResolutionLevel { Key = LevelKey.Mip(mip), Extent = new Extent3L(w, h, d) });
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            d = Math.Max(1, d / 2);
        }

        var topology = new ResourceTopology {
            SpatialDimensions = spatialDimensions,
            BaseExtent = baseExtent,
            ArrayElementCount = (int)header.ArraySize,
            FaceCount = header.IsCubemap ? 6 : 1,
            Levels = levels,
        };

        var (channels, representation) = DescribeFormat(header.Format, header.Width, header.Height);

        var part = new ImagePartDescriptor {
            Name = "texture",
            Spatial = spatial,
            Topology = topology,
            Channels = channels,
            Representation = representation,
            Metadata = MetadataCollection.Empty,
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static DdsHeader ToDdsHeader(ImagePartDescriptor part)
    {
        var format = DeriveFormat(part);
        var dimension = part.Topology.SpatialDimensions == 3 ? D3d10ResourceDimension.Texture3D : D3d10ResourceDimension.Texture2D;

        return new DdsHeader {
            Width = (uint)part.Topology.BaseExtent.Width,
            Height = (uint)part.Topology.BaseExtent.Height,
            Depth = dimension == D3d10ResourceDimension.Texture3D ? (uint)part.Topology.BaseExtent.Depth : 1,
            MipMapCount = (uint)Math.Max(1, part.Topology.Levels.Count),
            ArraySize = (uint)Math.Max(1, part.Topology.ArrayElementCount),
            IsCubemap = part.Topology.FaceCount == 6,
            Dimension = dimension,
            Format = format,
        };
    }

    private static (ChannelSchema Channels, PayloadRepresentation Representation) DescribeFormat(DxgiFormat format, long width, long height)
    {
        var extent = new Extent3L(width, height, 1);

        return format switch {
            DxgiFormat.R8Unorm => RawPlane(extent, ("R", SampleType.UNorm8)),
            DxgiFormat.R8G8Unorm => RawPlane(extent, ("R", SampleType.UNorm8), ("G", SampleType.UNorm8)),
            DxgiFormat.R8G8B8A8Unorm => RawPlane(extent, ("R", SampleType.UNorm8), ("G", SampleType.UNorm8), ("B", SampleType.UNorm8), ("A", SampleType.UNorm8)),
            DxgiFormat.B8G8R8A8Unorm => RawPlane(extent, ("B", SampleType.UNorm8), ("G", SampleType.UNorm8), ("R", SampleType.UNorm8), ("A", SampleType.UNorm8)),
            DxgiFormat.R16Unorm => RawPlane(extent, ("R", SampleType.UNorm16)),
            DxgiFormat.R16G16Unorm => RawPlane(extent, ("R", SampleType.UNorm16), ("G", SampleType.UNorm16)),
            DxgiFormat.R16G16B16A16Unorm => RawPlane(extent, ("R", SampleType.UNorm16), ("G", SampleType.UNorm16), ("B", SampleType.UNorm16), ("A", SampleType.UNorm16)),
            DxgiFormat.R16Float => RawPlane(extent, ("R", SampleType.Float16)),
            DxgiFormat.R16G16Float => RawPlane(extent, ("R", SampleType.Float16), ("G", SampleType.Float16)),
            DxgiFormat.R16G16B16A16Float => RawPlane(extent, ("R", SampleType.Float16), ("G", SampleType.Float16), ("B", SampleType.Float16), ("A", SampleType.Float16)),
            DxgiFormat.R32Float => RawPlane(extent, ("R", SampleType.Float32)),
            DxgiFormat.R32G32Float => RawPlane(extent, ("R", SampleType.Float32), ("G", SampleType.Float32)),
            DxgiFormat.R32G32B32A32Float => RawPlane(extent, ("R", SampleType.Float32), ("G", SampleType.Float32), ("B", SampleType.Float32), ("A", SampleType.Float32)),
            DxgiFormat.R10G10B10A2Unorm => Packed(
                EncodedFormatId.R10G10B10A2,
                [("R", SampleType.UNorm16), ("G", SampleType.UNorm16), ("B", SampleType.UNorm16), ("A", SampleType.UNorm8)],
                [new PackedField("R", 0, 10), new PackedField("G", 10, 10), new PackedField("B", 20, 10), new PackedField("A", 30, 2)]),
            DxgiFormat.R11G11B10Float => Packed(
                EncodedFormatId.R11G11B10Float,
                [("R", SampleType.Float16), ("G", SampleType.Float16), ("B", SampleType.Float16)],
                [new PackedField("R", 0, 11), new PackedField("G", 11, 11), new PackedField("B", 22, 10)]),
            DxgiFormat.Bc1Unorm => BlockCompressed(EncodedFormatId.Bc1, 64, ["R", "G", "B", "A"]),
            DxgiFormat.Bc2Unorm => BlockCompressed(EncodedFormatId.Bc2, 128, ["R", "G", "B", "A"]),
            DxgiFormat.Bc3Unorm => BlockCompressed(EncodedFormatId.Bc3, 128, ["R", "G", "B", "A"]),
            DxgiFormat.Bc4Unorm => BlockCompressed(EncodedFormatId.Bc4, 64, ["R"]),
            DxgiFormat.Bc5Unorm => BlockCompressed(EncodedFormatId.Bc5, 128, ["R", "G"]),
            DxgiFormat.Bc6HUf16 => BlockCompressed(EncodedFormatId.Bc6H, 128, ["R", "G", "B"]),
            DxgiFormat.Bc7Unorm => BlockCompressed(EncodedFormatId.Bc7, 128, ["R", "G", "B", "A"]),
            _ => throw new NotSupportedException($"DXGI format {format} is not describable yet."),
        };
    }

    private static DxgiFormat DeriveFormat(ImagePartDescriptor part)
    {
        if (part.Representation is EncodedElementRepresentation encoded) {
            if (encoded.Class == EncodedElementClass.BlockCompressed) {
                return encoded.Format.Name switch {
                    nameof(EncodedFormatId.Bc1) => DxgiFormat.Bc1Unorm,
                    nameof(EncodedFormatId.Bc2) => DxgiFormat.Bc2Unorm,
                    nameof(EncodedFormatId.Bc3) => DxgiFormat.Bc3Unorm,
                    nameof(EncodedFormatId.Bc4) => DxgiFormat.Bc4Unorm,
                    nameof(EncodedFormatId.Bc5) => DxgiFormat.Bc5Unorm,
                    nameof(EncodedFormatId.Bc6H) => DxgiFormat.Bc6HUf16,
                    nameof(EncodedFormatId.Bc7) => DxgiFormat.Bc7Unorm,
                    _ => throw new NotSupportedException($"Encoded format '{encoded.Format}' has no DDS equivalent."),
                };
            }

            return encoded.Format.Name switch {
                nameof(EncodedFormatId.R10G10B10A2) => DxgiFormat.R10G10B10A2Unorm,
                nameof(EncodedFormatId.R11G11B10Float) => DxgiFormat.R11G11B10Float,
                _ => throw new NotSupportedException($"Encoded format '{encoded.Format}' has no DDS equivalent."),
            };
        }

        var names = part.Channels.Channels.Select(c => c.Name.FullName).ToList();
        var sampleType = part.Channels.Channels[0].SampleType;

        return (names, sampleType) switch {
            (["R"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => DxgiFormat.R8Unorm,
            (["R", "G"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => DxgiFormat.R8G8Unorm,
            (["R", "G", "B", "A"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => DxgiFormat.R8G8B8A8Unorm,
            (["B", "G", "R", "A"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => DxgiFormat.B8G8R8A8Unorm,
            (["R"], { Kind: ScalarKind.UnsignedInt, Bits: 16 }) => DxgiFormat.R16Unorm,
            (["R", "G"], { Kind: ScalarKind.UnsignedInt, Bits: 16 }) => DxgiFormat.R16G16Unorm,
            (["R", "G", "B", "A"], { Kind: ScalarKind.UnsignedInt, Bits: 16 }) => DxgiFormat.R16G16B16A16Unorm,
            (["R"], { Kind: ScalarKind.Float, Bits: 16 }) => DxgiFormat.R16Float,
            (["R", "G"], { Kind: ScalarKind.Float, Bits: 16 }) => DxgiFormat.R16G16Float,
            (["R", "G", "B", "A"], { Kind: ScalarKind.Float, Bits: 16 }) => DxgiFormat.R16G16B16A16Float,
            (["R"], { Kind: ScalarKind.Float, Bits: 32 }) => DxgiFormat.R32Float,
            (["R", "G"], { Kind: ScalarKind.Float, Bits: 32 }) => DxgiFormat.R32G32Float,
            (["R", "G", "B", "A"], { Kind: ScalarKind.Float, Bits: 32 }) => DxgiFormat.R32G32B32A32Float,
            _ => throw new NotSupportedException($"Channel layout [{string.Join(",", names)}] has no DDS raw format equivalent."),
        };
    }

    private static (ChannelSchema, PayloadRepresentation) RawPlane(Extent3L extent, params (string Name, SampleType Type)[] channels)
    {
        var descriptors = channels.Select(c => new ChannelDescriptor {
            Name = c.Name,
            Semantic = ChannelSemanticFor(c.Name),
            SampleType = c.Type,
            Sampling = SampleGrid.Unit,
        }).ToList();

        var plane = new SamplePlaneDescriptor {
            Channels = channels.Select(c => (ChannelPath)c.Name).ToList(),
            Extent = extent,
            Layout = PlaneLayout.Interleaved,
        };

        return (new ChannelSchema { Channels = descriptors }, new PlainSampleRepresentation { Planes = [plane] });
    }

    private static (ChannelSchema, PayloadRepresentation) Packed(EncodedFormatId format, (string Name, SampleType Type)[] channels, PackedField[] fields)
    {
        var descriptors = channels.Select(c => new ChannelDescriptor {
            Name = c.Name,
            Semantic = ChannelSemanticFor(c.Name),
            SampleType = c.Type,
            Sampling = SampleGrid.Unit,
        }).ToList();

        var representation = new EncodedElementRepresentation {
            Format = format,
            TexelExtentPerElement = new Extent3I(1, 1, 1),
            BitsPerElement = fields.Sum(f => f.Bits),
            Class = EncodedElementClass.Packed,
            PackedLayout = new PackedFieldLayout { Fields = fields },
        };

        return (new ChannelSchema { Channels = descriptors }, representation);
    }

    private static (ChannelSchema, PayloadRepresentation) BlockCompressed(EncodedFormatId format, int bitsPerBlock, string[] channelNames)
    {
        var descriptors = channelNames.Select(name => new ChannelDescriptor {
            Name = name,
            Semantic = ChannelSemanticFor(name),
            SampleType = SampleType.UNorm8,
            Sampling = SampleGrid.Unit,
        }).ToList();

        var representation = new EncodedElementRepresentation {
            Format = format,
            TexelExtentPerElement = new Extent3I(4, 4, 1),
            BitsPerElement = bitsPerBlock,
            Class = EncodedElementClass.BlockCompressed,
        };

        return (new ChannelSchema { Channels = descriptors }, representation);
    }

    private static ChannelSemantic ChannelSemanticFor(string name) => name switch {
        "R" => ChannelSemantic.Red,
        "G" => ChannelSemantic.Green,
        "B" => ChannelSemantic.Blue,
        "A" => ChannelSemantic.Alpha,
        _ => ChannelSemantic.Custom,
    };
}
