using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Ktx2.Format;

namespace Lucitex.Ktx2;

internal static class Ktx2DescriptorMapper
{
    private const string k_MetadataNamespace = "ktx";

    public static ImageAssetDescriptor ToImageAssetDescriptor(Ktx2Header header, IReadOnlyList<Ktx2KeyValueEntry> keyValues)
    {
        var window = ImageBox.FromOrigin(header.PixelWidth, header.PixelHeight);

        var spatial = new SpatialDomain {
            DataWindow = window,
            DisplayWindow = window,
            Orientation = LogicalOrientation.Identity,
            Traversal = StorageTraversal.IncreasingY,
        };

        var spatialDimensions = header.Is3D ? 3 : 2;
        var baseExtent = new Extent3L(header.PixelWidth, header.PixelHeight, header.Is3D ? header.PixelDepth : 1);

        var levels = new List<ResolutionLevel>();
        var w = (long)header.PixelWidth;
        var h = (long)header.PixelHeight;
        var d = spatialDimensions == 3 ? (long)header.PixelDepth : 1;
        for (var mip = 0; mip < header.Levels.Count; mip++) {
            levels.Add(new ResolutionLevel { Key = LevelKey.Mip(mip), Extent = new Extent3L(w, h, d) });
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            d = Math.Max(1, d / 2);
        }

        var topology = new ResourceTopology {
            SpatialDimensions = spatialDimensions,
            BaseExtent = baseExtent,
            ArrayElementCount = header.EffectiveArrayElementCount,
            FaceCount = header.IsCubemap ? 6 : 1,
            Levels = levels,
        };

        var (channels, representation) = DescribeFormat(header.Format, header.PixelWidth, header.PixelHeight);

        var part = new ImagePartDescriptor {
            Name = "texture",
            Spatial = spatial,
            Topology = topology,
            Channels = channels,
            Representation = representation,
            Metadata = ToMetadata(keyValues),
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    public static Ktx2Header ToKtx2Header(ImagePartDescriptor part, Ktx2SupercompressionScheme scheme, IReadOnlyList<Ktx2LevelIndexEntry> levels)
    {
        var format = DeriveFormat(part);
        var is3D = part.Topology.SpatialDimensions == 3;

        return new Ktx2Header {
            Format = format,
            PixelWidth = (uint)part.Topology.BaseExtent.Width,
            PixelHeight = (uint)part.Topology.BaseExtent.Height,
            PixelDepth = is3D ? (uint)part.Topology.BaseExtent.Depth : 0,
            LayerCount = part.Topology.ArrayElementCount > 1 ? (uint)part.Topology.ArrayElementCount : 0,
            FaceCount = (uint)(part.Topology.FaceCount == 6 ? 6 : 1),
            Levels = levels,
            SupercompressionScheme = scheme,
        };
    }

    public static IReadOnlyList<Ktx2KeyValueEntry> ToKeyValueEntries(ImagePartDescriptor part) => part.Metadata.Entries
        .Where(e => e.Namespace == k_MetadataNamespace)
        .Select(e => new Ktx2KeyValueEntry(e.Name, e.RawRepresentation ?? []))
        .ToList();

    private static MetadataCollection ToMetadata(IReadOnlyList<Ktx2KeyValueEntry> keyValues)
    {
        var entries = keyValues
            .Select(kv => new MetadataEntry { Namespace = k_MetadataNamespace, Name = kv.Key, RawRepresentation = kv.Value })
            .ToList();

        return new MetadataCollection { Entries = entries };
    }

    private static (ChannelSchema Channels, PayloadRepresentation Representation) DescribeFormat(VkFormat format, long width, long height)
    {
        var extent = new Extent3L(width, height, 1);

        return format switch {
            VkFormat.R8Unorm => RawPlane(extent, ("R", SampleType.UNorm8)),
            VkFormat.R8G8Unorm => RawPlane(extent, ("R", SampleType.UNorm8), ("G", SampleType.UNorm8)),
            VkFormat.R8G8B8A8Unorm => RawPlane(extent, ("R", SampleType.UNorm8), ("G", SampleType.UNorm8), ("B", SampleType.UNorm8), ("A", SampleType.UNorm8)),
            VkFormat.B8G8R8A8Unorm => RawPlane(extent, ("B", SampleType.UNorm8), ("G", SampleType.UNorm8), ("R", SampleType.UNorm8), ("A", SampleType.UNorm8)),
            VkFormat.R16Unorm => RawPlane(extent, ("R", SampleType.UNorm16)),
            VkFormat.R16G16Unorm => RawPlane(extent, ("R", SampleType.UNorm16), ("G", SampleType.UNorm16)),
            VkFormat.R16G16B16A16Unorm => RawPlane(extent, ("R", SampleType.UNorm16), ("G", SampleType.UNorm16), ("B", SampleType.UNorm16), ("A", SampleType.UNorm16)),
            VkFormat.R16Sfloat => RawPlane(extent, ("R", SampleType.Float16)),
            VkFormat.R16G16Sfloat => RawPlane(extent, ("R", SampleType.Float16), ("G", SampleType.Float16)),
            VkFormat.R16G16B16A16Sfloat => RawPlane(extent, ("R", SampleType.Float16), ("G", SampleType.Float16), ("B", SampleType.Float16), ("A", SampleType.Float16)),
            VkFormat.R32Sfloat => RawPlane(extent, ("R", SampleType.Float32)),
            VkFormat.R32G32Sfloat => RawPlane(extent, ("R", SampleType.Float32), ("G", SampleType.Float32)),
            VkFormat.R32G32B32A32Sfloat => RawPlane(extent, ("R", SampleType.Float32), ("G", SampleType.Float32), ("B", SampleType.Float32), ("A", SampleType.Float32)),
            VkFormat.A2B10G10R10Unorm => Packed(
                EncodedFormatId.R10G10B10A2,
                [("R", SampleType.UNorm16), ("G", SampleType.UNorm16), ("B", SampleType.UNorm16), ("A", SampleType.UNorm8)],
                [new PackedField("R", 0, 10), new PackedField("G", 10, 10), new PackedField("B", 20, 10), new PackedField("A", 30, 2)]),
            VkFormat.B10G11R11Ufloat => Packed(
                EncodedFormatId.R11G11B10Float,
                [("R", SampleType.Float16), ("G", SampleType.Float16), ("B", SampleType.Float16)],
                [new PackedField("R", 0, 11), new PackedField("G", 11, 11), new PackedField("B", 22, 10)]),
            VkFormat.E5B9G9R9Ufloat => Packed(
                EncodedFormatId.Rgb9E5,
                [("R", SampleType.Float16), ("G", SampleType.Float16), ("B", SampleType.Float16)],
                [new PackedField("R", 0, 9), new PackedField("G", 9, 9), new PackedField("B", 18, 9), new PackedField("E", 27, 5)]),
            VkFormat.Bc1RgbaUnormBlock => BlockCompressed(EncodedFormatId.Bc1, 64, ["R", "G", "B", "A"]),
            VkFormat.Bc2UnormBlock => BlockCompressed(EncodedFormatId.Bc2, 128, ["R", "G", "B", "A"]),
            VkFormat.Bc3UnormBlock => BlockCompressed(EncodedFormatId.Bc3, 128, ["R", "G", "B", "A"]),
            VkFormat.Bc4UnormBlock => BlockCompressed(EncodedFormatId.Bc4, 64, ["R"]),
            VkFormat.Bc5UnormBlock => BlockCompressed(EncodedFormatId.Bc5, 128, ["R", "G"]),
            VkFormat.Bc6HUfloatBlock => BlockCompressed(EncodedFormatId.Bc6H, 128, ["R", "G", "B"], SampleType.Float16),
            VkFormat.Bc6HSfloatBlock => BlockCompressed(EncodedFormatId.Bc6HSigned, 128, ["R", "G", "B"], SampleType.Float16),
            VkFormat.Bc7UnormBlock => BlockCompressed(EncodedFormatId.Bc7, 128, ["R", "G", "B", "A"]),
            _ => throw new NotSupportedException($"VkFormat {format} is not describable yet."),
        };
    }

    private static VkFormat DeriveFormat(ImagePartDescriptor part)
    {
        if (part.Representation is EncodedElementRepresentation encoded) {
            if (encoded.Class == EncodedElementClass.BlockCompressed) {
                return encoded.Format.Name switch {
                    nameof(EncodedFormatId.Bc1) => VkFormat.Bc1RgbaUnormBlock,
                    nameof(EncodedFormatId.Bc2) => VkFormat.Bc2UnormBlock,
                    nameof(EncodedFormatId.Bc3) => VkFormat.Bc3UnormBlock,
                    nameof(EncodedFormatId.Bc4) => VkFormat.Bc4UnormBlock,
                    nameof(EncodedFormatId.Bc5) => VkFormat.Bc5UnormBlock,
                    nameof(EncodedFormatId.Bc6H) => VkFormat.Bc6HUfloatBlock,
                    nameof(EncodedFormatId.Bc6HSigned) => VkFormat.Bc6HSfloatBlock,
                    nameof(EncodedFormatId.Bc7) => VkFormat.Bc7UnormBlock,
                    _ => throw new NotSupportedException($"Encoded format '{encoded.Format}' has no KTX2 equivalent."),
                };
            }

            return encoded.Format.Name switch {
                nameof(EncodedFormatId.R10G10B10A2) => VkFormat.A2B10G10R10Unorm,
                nameof(EncodedFormatId.R11G11B10Float) => VkFormat.B10G11R11Ufloat,
                nameof(EncodedFormatId.Rgb9E5) => VkFormat.E5B9G9R9Ufloat,
                _ => throw new NotSupportedException($"Encoded format '{encoded.Format}' has no KTX2 equivalent."),
            };
        }

        var names = part.Channels.Channels.Select(c => c.Name.FullName).ToList();
        var sampleType = part.Channels.Channels[0].SampleType;

        return (names, sampleType) switch {
            (["R"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => VkFormat.R8Unorm,
            (["R", "G"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => VkFormat.R8G8Unorm,
            (["R", "G", "B", "A"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => VkFormat.R8G8B8A8Unorm,
            (["B", "G", "R", "A"], { Kind: ScalarKind.UnsignedInt, Bits: 8 }) => VkFormat.B8G8R8A8Unorm,
            (["R"], { Kind: ScalarKind.UnsignedInt, Bits: 16 }) => VkFormat.R16Unorm,
            (["R", "G"], { Kind: ScalarKind.UnsignedInt, Bits: 16 }) => VkFormat.R16G16Unorm,
            (["R", "G", "B", "A"], { Kind: ScalarKind.UnsignedInt, Bits: 16 }) => VkFormat.R16G16B16A16Unorm,
            (["R"], { Kind: ScalarKind.Float, Bits: 16 }) => VkFormat.R16Sfloat,
            (["R", "G"], { Kind: ScalarKind.Float, Bits: 16 }) => VkFormat.R16G16Sfloat,
            (["R", "G", "B", "A"], { Kind: ScalarKind.Float, Bits: 16 }) => VkFormat.R16G16B16A16Sfloat,
            (["R"], { Kind: ScalarKind.Float, Bits: 32 }) => VkFormat.R32Sfloat,
            (["R", "G"], { Kind: ScalarKind.Float, Bits: 32 }) => VkFormat.R32G32Sfloat,
            (["R", "G", "B", "A"], { Kind: ScalarKind.Float, Bits: 32 }) => VkFormat.R32G32B32A32Sfloat,
            _ => throw new NotSupportedException($"Channel layout [{string.Join(",", names)}] has no KTX2 raw format equivalent."),
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
            Class = format.Name == nameof(EncodedFormatId.Rgb9E5) ? EncodedElementClass.SharedExponent : EncodedElementClass.Packed,
            PackedLayout = new PackedFieldLayout { Fields = fields },
        };

        return (new ChannelSchema { Channels = descriptors }, representation);
    }

    private static (ChannelSchema, PayloadRepresentation) BlockCompressed(
        EncodedFormatId format,
        int bitsPerBlock,
        string[] channelNames,
        SampleType? sampleType = null)
    {
        var descriptors = channelNames.Select(name => new ChannelDescriptor {
            Name = name,
            Semantic = ChannelSemanticFor(name),
            SampleType = sampleType ?? SampleType.UNorm8,
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
