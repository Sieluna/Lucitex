using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Webp;

internal static class WebpDescriptorMapper
{
    public static ImageAssetDescriptor Describe(WebpDocument document)
    {
        var extent = new Extent3L(document.Width, document.Height, 1);
        ChannelPath[] names = ["R", "G", "B", "A"];
        var entries = new List<MetadataEntry>();
        if (document.Metadata.Exif is { } exif) {
            entries.Add(new MetadataEntry { Namespace = "webp", Name = "EXIF", RawRepresentation = exif });
        }
        if (document.Metadata.Xmp is { } xmp) {
            entries.Add(new MetadataEntry { Namespace = "webp", Name = "XMP", RawRepresentation = xmp });
        }
        return new ImageAssetDescriptor {
            Parts = [new ImagePartDescriptor {
                Name = "image",
                Spatial = new SpatialDomain {
                    DataWindow = ImageBox.FromOrigin(document.Width, document.Height),
                    DisplayWindow = ImageBox.FromOrigin(document.Width, document.Height),
                    Orientation = LogicalOrientation.Identity,
                    Traversal = StorageTraversal.IncreasingY,
                },
                Topology = new ResourceTopology { BaseExtent = extent, Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = extent }] },
                Channels = new ChannelSchema { Channels = names.Select(name => new ChannelDescriptor { Name = name, SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit }).ToArray() },
                Representation = new PlainSampleRepresentation { Planes = [new SamplePlaneDescriptor { Channels = names, Extent = extent }] },
                Alpha = new AlphaDescriptor { Mode = AlphaMode.Straight },
                Color = new ColorEncoding { Transfer = document.Metadata.Icc is null ? TransferFunction.Srgb : TransferFunction.Unknown, IccProfile = document.Metadata.Icc },
                Metadata = new MetadataCollection { Entries = entries },
            }],
        };
    }

    public static (int Width, int Height, int Channels, WebpMetadata Metadata) Validate(ImageAssetDescriptor descriptor)
    {
        if (descriptor.Parts.Count != 1) {
            throw new NotSupportedException("Static WebP requires exactly one image part.");
        }
        var part = descriptor.Parts[0];
        var topology = part.Topology;
        var extent = topology.BaseExtent;
        if (extent.Width is < 1 or > 16384 || extent.Height is < 1 or > 16384) {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "VP8L dimensions must be between 1 and 16384.");
        }
        if (topology.SpatialDimensions != 2 || extent.Depth != 1 || topology.ArrayElementCount != 1 || topology.FaceCount != 1 || topology.Levels.Count != 1 || topology.Levels[0].Key != LevelKey.Base || topology.Levels[0].Extent != extent || topology.Levels[0].Supercompression is not null) {
            throw new NotSupportedException("Static WebP requires a single two-dimensional base level.");
        }
        var window = ImageBox.FromOrigin(extent.Width, extent.Height);
        if (part.Spatial.DataWindow != window || part.Spatial.DisplayWindow != window || part.Spatial.Orientation != LogicalOrientation.Identity || part.Spatial.PixelAspectRatio != 1) {
            throw new NotSupportedException("WebP requires an origin-based image with identity orientation and square pixels.");
        }
        var channels = part.Channels.Channels;
        string[] names = channels.Count == 3 ? ["R", "G", "B"] : ["R", "G", "B", "A"];
        if (channels.Count != names.Length || channels.Where((channel, i) => channel.Name.FullName != names[i] || channel.SampleType != SampleType.UNorm8 || channel.Sampling != SampleGrid.Unit).Any() ||
            part.Representation is not PlainSampleRepresentation { Planes.Count: 1 } plain || plain.Planes[0].Layout != PlaneLayout.Interleaved || plain.Planes[0].Extent != extent || !plain.Planes[0].Channels.Select(channel => channel.FullName).SequenceEqual(names)) {
            throw new NotSupportedException("WebP encoding requires interleaved RGB8 or RGBA8 samples.");
        }
        if (part.Alpha?.Mode == AlphaMode.Premultiplied) {
            throw new NotSupportedException("Lossless WebP encoding requires straight alpha.");
        }
        if (part.Color?.Transfer is not (null or TransferFunction.Unknown or TransferFunction.Srgb) && part.Color.IccProfile is null) {
            throw new NotSupportedException("WebP requires sRGB samples or an ICC profile describing their color encoding.");
        }
        var exif = part.Metadata.Entries.FirstOrDefault(entry => entry.Namespace == "webp" && entry.Name == "EXIF")?.RawRepresentation;
        var xmp = part.Metadata.Entries.FirstOrDefault(entry => entry.Namespace == "webp" && entry.Name == "XMP")?.RawRepresentation;
        return ((int)extent.Width, (int)extent.Height, channels.Count, new WebpMetadata(part.Color?.IccProfile, exif, xmp));
    }

    public static int ValidateRows(WorkRegion region, int width, int height, int channels)
    {
        if (region.Subresource != new SubresourceId(0, 0, 0, LevelKey.Base)) {
            throw new ArgumentOutOfRangeException(nameof(region), "WebP contains only one base-level subresource.");
        }
        var box = region.Region;
        if (box.MinY < 0 || box.MaxYExclusive < box.MinY || box.MaxYExclusive > height) {
            throw new ArgumentOutOfRangeException(nameof(region));
        }
        if (box.MinX != 0 || box.MaxXExclusive != width) {
            throw new NotSupportedException("WebP regions must contain complete rows.");
        }
        return checked((int)box.Height * width * channels);
    }
}
