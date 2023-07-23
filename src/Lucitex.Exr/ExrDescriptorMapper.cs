using Lucitex.Core.Color;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal static class ExrDescriptorMapper
{
    private const string MetadataNamespace = "exr";

    public static ImageAssetDescriptor ToImageAssetDescriptor(IReadOnlyList<ExrHeader> headers) =>
        new() { Parts = headers.Select(ToImagePartDescriptor).ToList() };

    public static ImagePartDescriptor ToImagePartDescriptor(ExrHeader header)
    {
        var dataWindow = ImageBox.FromInclusive(
            header.DataWindow.XMin, header.DataWindow.YMin,
            header.DataWindow.XMax, header.DataWindow.YMax);

        var displayWindow = ImageBox.FromInclusive(
            header.DisplayWindow.XMin, header.DisplayWindow.YMin,
            header.DisplayWindow.XMax, header.DisplayWindow.YMax);

        var spatial = new SpatialDomain
        {
            DataWindow = dataWindow,
            DisplayWindow = displayWindow,
            Orientation = LogicalOrientation.Identity,
            Traversal = ToStorageTraversal(header.LineOrder),
            PixelAspectRatio = header.PixelAspectRatio,
        };

        var channelDescriptors = new List<ChannelDescriptor>();
        var planes = new List<SamplePlaneDescriptor>();

        foreach (var channel in header.Channels)
        {
            var planeWidth = (dataWindow.Width + channel.XSampling - 1) / channel.XSampling;
            var planeHeight = (dataWindow.Height + channel.YSampling - 1) / channel.YSampling;

            channelDescriptors.Add(new ChannelDescriptor
            {
                Name = channel.Name,
                Semantic = ToSemantic(channel.Name),
                SampleType = ToSampleType(channel.PixelType),
                Sampling = new SampleGrid { Origin = Long3.Zero, Step = new Int3(channel.XSampling, channel.YSampling, 1) },
            });

            planes.Add(new SamplePlaneDescriptor
            {
                Channels = [channel.Name],
                Extent = new Extent3L(planeWidth, planeHeight, 1),
                Layout = PlaneLayout.Planar,
            });
        }

        var topology = new ResourceTopology
        {
            SpatialDimensions = 2,
            BaseExtent = new Extent3L(dataWindow.Width, dataWindow.Height, 1),
            Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(dataWindow.Width, dataWindow.Height, 1) }],
        };

        var alpha = header.Channels.Any(c => c.Name == "A")
            ? new AlphaDescriptor { Mode = AlphaMode.Straight }
            : null;

        return new ImagePartDescriptor
        {
            Name = header.PartName,
            Spatial = spatial,
            Topology = topology,
            Channels = new ChannelSchema { Channels = channelDescriptors },
            Representation = new PlainSampleRepresentation { Planes = planes },
            Alpha = alpha,
            Metadata = ToMetadata(header),
        };
    }

    public static ExrHeader ToExrHeader(ImageAssetDescriptor asset, ExrCompressionId compression) =>
        ToExrHeader(asset.Parts[0], compression);

    public static ExrHeader ToExrHeader(ImagePartDescriptor part, ExrCompressionId compression)
    {
        var dataWindow = ToExrBox2i(part.Spatial.DataWindow);
        var displayWindow = ToExrBox2i(part.Spatial.DisplayWindow);

        var channels = part.Channels.Channels
            .Select(c => new ExrChannelInfo
            {
                Name = c.Name.FullName,
                PixelType = ToPixelType(c.SampleType),
                XSampling = c.Sampling.Step.X,
                YSampling = c.Sampling.Step.Y,
            })
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        return new ExrHeader
        {
            Channels = channels,
            Compression = compression,
            DataWindow = dataWindow,
            DisplayWindow = displayWindow,
            LineOrder = ToLineOrder(part.Spatial.Traversal),
            PixelAspectRatio = (float)part.Spatial.PixelAspectRatio,
            PartName = part.Name,
        };
    }

    private static MetadataCollection ToMetadata(ExrHeader header)
    {
        var entries = header.UnknownAttributes
            .Select(attribute => new MetadataEntry
            {
                Namespace = MetadataNamespace,
                Name = attribute.Name,
                TypedValue = new StringMetadataValue(attribute.Type),
                RawRepresentation = attribute.Value,
            })
            .ToList();

        return new MetadataCollection { Entries = entries };
    }

    private static ExrBox2i ToExrBox2i(ImageBox box) =>
        new((int)box.MinX, (int)box.MinY, (int)(box.MaxXExclusive - 1), (int)(box.MaxYExclusive - 1));

    private static StorageTraversal ToStorageTraversal(ExrLineOrder lineOrder) => lineOrder switch
    {
        ExrLineOrder.IncreasingY => StorageTraversal.IncreasingY,
        ExrLineOrder.DecreasingY => StorageTraversal.DecreasingY,
        ExrLineOrder.RandomY => StorageTraversal.Random,
        _ => StorageTraversal.CodecDefined,
    };

    private static ExrLineOrder ToLineOrder(StorageTraversal traversal) => traversal switch
    {
        StorageTraversal.IncreasingY => ExrLineOrder.IncreasingY,
        StorageTraversal.DecreasingY => ExrLineOrder.DecreasingY,
        StorageTraversal.Random => ExrLineOrder.RandomY,
        _ => ExrLineOrder.IncreasingY,
    };

    private static SampleType ToSampleType(ExrPixelType pixelType) => pixelType switch
    {
        ExrPixelType.UInt => SampleType.UInt32,
        ExrPixelType.Half => SampleType.Float16,
        ExrPixelType.Float => SampleType.Float32,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelType)),
    };

    private static ExrPixelType ToPixelType(SampleType sampleType) => sampleType switch
    {
        { Kind: ScalarKind.UnsignedInt, Bits: 32 } => ExrPixelType.UInt,
        { Kind: ScalarKind.Float, Bits: 16 } => ExrPixelType.Half,
        { Kind: ScalarKind.Float, Bits: 32 } => ExrPixelType.Float,
        _ => throw new NotSupportedException($"Sample type {sampleType} has no EXR pixel type equivalent."),
    };

    private static ChannelSemantic ToSemantic(string channelName) => channelName switch
    {
        "R" => ChannelSemantic.Red,
        "G" => ChannelSemantic.Green,
        "B" => ChannelSemantic.Blue,
        "A" => ChannelSemantic.Alpha,
        "Y" => ChannelSemantic.Luminance,
        "RY" => ChannelSemantic.ChromaRedDiff,
        "BY" => ChannelSemantic.ChromaBlueDiff,
        "Z" => ChannelSemantic.Depth,
        _ => ChannelSemantic.Custom,
    };
}
