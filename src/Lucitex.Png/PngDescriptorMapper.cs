using Lucitex.Core.Color;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Png.Format;

namespace Lucitex.Png;

internal static class PngDescriptorMapper
{
    private const string k_MetadataNamespace = "png";

    public static ImageAssetDescriptor ToImageAssetDescriptor(PngDocument document)
    {
        var ihdr = document.Ihdr;
        var window = ImageBox.FromOrigin(ihdr.Width, ihdr.Height);

        var spatial = new SpatialDomain {
            DataWindow = window,
            DisplayWindow = window,
            Orientation = LogicalOrientation.Identity,
            Traversal = ihdr.Interlace == PngInterlaceMethod.None ? StorageTraversal.IncreasingY : StorageTraversal.CodecDefined,
        };

        var topology = new ResourceTopology {
            SpatialDimensions = 2,
            BaseExtent = new Extent3L(ihdr.Width, ihdr.Height, 1),
            Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(ihdr.Width, ihdr.Height, 1) }],
        };

        var (channels, representation, alpha) = BuildChannelsAndRepresentation(document);

        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = spatial,
            Topology = topology,
            Channels = channels,
            Representation = representation,
            Alpha = alpha,
            Color = BuildColorEncoding(document),
            Metadata = BuildMetadata(document),
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static (ChannelSchema Channels, PayloadRepresentation Representation, AlphaDescriptor? Alpha) BuildChannelsAndRepresentation(PngDocument document)
    {
        var ihdr = document.Ihdr;

        if (ihdr.ColorType == PngColorType.Indexed) {
            var indexSampleType = SampleTypeForBitDepth(ihdr.BitDepth);
            var hasAlpha = document.TransparencyData is { Length: > 0 };

            var entryChannels = new List<ChannelDescriptor>
            {
                Channel("R", SampleType.UNorm8),
                Channel("G", SampleType.UNorm8),
                Channel("B", SampleType.UNorm8),
            };

            if (hasAlpha) {
                entryChannels.Add(Channel("A", SampleType.UNorm8));
            }

            var channelsPerEntry = entryChannels.Count;
            var rawEntries = new byte[document.Palette.Count * channelsPerEntry];
            for (var i = 0; i < document.Palette.Count; i++) {
                var entryOffset = i * channelsPerEntry;
                rawEntries[entryOffset + 0] = document.Palette[i].R;
                rawEntries[entryOffset + 1] = document.Palette[i].G;
                rawEntries[entryOffset + 2] = document.Palette[i].B;
                if (hasAlpha) {
                    rawEntries[entryOffset + 3] = document.TransparencyData is { } transparency && i < transparency.Length ? transparency[i] : (byte)255;
                }
            }

            var representation = new IndexedRepresentation {
                IndexType = indexSampleType,
                Palette = new PaletteDescriptor {
                    EntryCount = document.Palette.Count,
                    EntryChannels = new ChannelSchema { Channels = entryChannels },
                    EntrySampleType = SampleType.UNorm8,
                    RawEntries = rawEntries,
                },
            };

            var channels = new ChannelSchema { Channels = [Channel("Index", indexSampleType)] };
            var alpha = hasAlpha ? new AlphaDescriptor { Mode = AlphaMode.Straight } : null;

            return (channels, representation, alpha);
        }

        var sampleType = SampleTypeForBitDepth(ihdr.BitDepth);
        var names = ihdr.ColorType switch {
            PngColorType.Grayscale => new[] { "Y" },
            PngColorType.Truecolor => ["R", "G", "B"],
            PngColorType.GrayscaleAlpha => ["Y", "A"],
            PngColorType.TruecolorAlpha => ["R", "G", "B", "A"],
            _ => throw new ArgumentOutOfRangeException(nameof(document)),
        };

        var channelDescriptors = names.Select(n => Channel(n, sampleType)).ToList();
        var plane = new SamplePlaneDescriptor {
            Channels = names.Select(n => (Lucitex.Core.Sampling.ChannelPath)n).ToList(),
            Extent = new Extent3L(ihdr.Width, ihdr.Height, 1),
            Layout = PlaneLayout.Interleaved,
        };

        var hasAlphaChannel = ihdr.ColorType is PngColorType.GrayscaleAlpha or PngColorType.TruecolorAlpha;
        var alphaDescriptor = hasAlphaChannel ? new AlphaDescriptor { Mode = AlphaMode.Straight } : null;

        return (new ChannelSchema { Channels = channelDescriptors }, new PlainSampleRepresentation { Planes = [plane] }, alphaDescriptor);
    }

    private static ColorEncoding? BuildColorEncoding(PngDocument document)
    {
        if (document.SrgbRenderingIntent is null && document.Chromaticities is null && document.IccProfile is null) {
            return null;
        }

        Chromaticities? primaries = null;
        if (document.Chromaticities is { } c) {
            primaries = new Chromaticities {
                Red = new Chromaticity(c.Red.X, c.Red.Y),
                Green = new Chromaticity(c.Green.X, c.Green.Y),
                Blue = new Chromaticity(c.Blue.X, c.Blue.Y),
                White = new Chromaticity(c.White.X, c.White.Y),
            };
        }

        return new ColorEncoding {
            Transfer = document.SrgbRenderingIntent is not null ? TransferFunction.Srgb : TransferFunction.Unknown,
            Primaries = primaries,
            IccProfile = document.IccProfile,
        };
    }

    private static MetadataCollection BuildMetadata(PngDocument document)
    {
        var entries = new List<MetadataEntry>();

        if (document.Gamma is { } gamma) {
            entries.Add(new MetadataEntry {
                Namespace = k_MetadataNamespace,
                Name = "gAMA",
                TypedValue = new DoubleMetadataValue(gamma),
            });
        }

        foreach (var text in document.TextEntries) {
            entries.Add(new MetadataEntry {
                Namespace = k_MetadataNamespace,
                Name = text.Keyword,
                TypedValue = new StringMetadataValue(text.Text),
            });
        }

        if (document.Ihdr.ColorType == PngColorType.Indexed) {
            var paletteBytes = new byte[document.Palette.Count * 3];
            for (var i = 0; i < document.Palette.Count; i++) {
                paletteBytes[(i * 3) + 0] = document.Palette[i].R;
                paletteBytes[(i * 3) + 1] = document.Palette[i].G;
                paletteBytes[(i * 3) + 2] = document.Palette[i].B;
            }

            entries.Add(new MetadataEntry { Namespace = k_MetadataNamespace, Name = "PLTE", RawRepresentation = paletteBytes });
        }

        if (document.TransparencyData is { } transparencyData) {
            entries.Add(new MetadataEntry { Namespace = k_MetadataNamespace, Name = "tRNS", RawRepresentation = transparencyData });
        }

        foreach (var chunk in document.UnknownChunks) {
            entries.Add(new MetadataEntry {
                Namespace = k_MetadataNamespace,
                Name = chunk.Type,
                RawRepresentation = chunk.Data,
            });
        }

        return new MetadataCollection { Entries = entries };
    }

    public static PngDocument ToPngDocument(ImagePartDescriptor part)
    {
        var width = (int)part.Topology.BaseExtent.Width;
        var height = (int)part.Topology.BaseExtent.Height;

        var (colorType, bitDepth) = DeriveColorTypeAndBitDepth(part);

        var palette = colorType == PngColorType.Indexed ? ExtractPalette(part) : [];
        var transparency = FindRawMetadata(part, "tRNS");

        var ihdr = new PngIhdr(width, height, bitDepth, colorType, PngInterlaceMethod.None);

        float? gamma = null;
        byte? srgbIntent = null;
        PngChromaticities? chromaticities = null;
        var textEntries = new List<PngTextEntry>();

        foreach (var entry in part.Metadata.Entries) {
            if (entry.Namespace != k_MetadataNamespace) {
                continue;
            }

            switch (entry.Name) {
                case "gAMA" when entry.TypedValue is DoubleMetadataValue gammaValue:
                    gamma = (float)gammaValue.Value;
                    break;
                default:
                    if (entry.TypedValue is StringMetadataValue text) {
                        textEntries.Add(new PngTextEntry { Keyword = entry.Name, Text = text.Value });
                    }

                    break;
            }
        }

        if (part.Color?.Transfer == TransferFunction.Srgb) {
            srgbIntent = 0;
        }

        if (part.Color?.Primaries is { } primaries) {
            chromaticities = new PngChromaticities {
                White = new PngChromaticity(primaries.White.X, primaries.White.Y),
                Red = new PngChromaticity(primaries.Red.X, primaries.Red.Y),
                Green = new PngChromaticity(primaries.Green.X, primaries.Green.Y),
                Blue = new PngChromaticity(primaries.Blue.X, primaries.Blue.Y),
            };
        }

        return new PngDocument {
            Ihdr = ihdr,
            Palette = palette ?? [],
            TransparencyData = transparency,
            Gamma = gamma,
            SrgbRenderingIntent = srgbIntent,
            Chromaticities = chromaticities,
            IccProfile = part.Color?.IccProfile,
            TextEntries = textEntries,
        };
    }

    private static (PngColorType ColorType, byte BitDepth) DeriveColorTypeAndBitDepth(ImagePartDescriptor part)
    {
        if (part.Representation is IndexedRepresentation indexed) {
            return (PngColorType.Indexed, indexed.IndexType.Bits);
        }

        var names = part.Channels.Channels.Select(c => c.Name.FullName).ToList();
        var bitDepth = part.Channels.Channels[0].SampleType.Bits;

        var colorType = names switch {
            ["Y"] => PngColorType.Grayscale,
            ["Y", "A"] => PngColorType.GrayscaleAlpha,
            ["R", "G", "B"] => PngColorType.Truecolor,
            ["R", "G", "B", "A"] => PngColorType.TruecolorAlpha,
            _ => throw new NotSupportedException($"Channel layout [{string.Join(",", names)}] has no PNG color type equivalent."),
        };

        return (colorType, bitDepth);
    }

    private static IReadOnlyList<PngPaletteEntry> ExtractPalette(ImagePartDescriptor part)
    {
        var raw = FindRawMetadata(part, "PLTE")
            ?? throw new InvalidOperationException("Indexed PNG descriptor is missing 'png:PLTE' metadata with the palette bytes.");

        var entries = new List<PngPaletteEntry>(raw.Length / 3);
        for (var i = 0; i + 2 < raw.Length; i += 3) {
            entries.Add(new PngPaletteEntry(raw[i], raw[i + 1], raw[i + 2]));
        }

        return entries;
    }

    private static byte[]? FindRawMetadata(ImagePartDescriptor part, string name) => part.Metadata.Entries
        .FirstOrDefault(e => e.Namespace == k_MetadataNamespace && e.Name == name)?.RawRepresentation;

    private static ChannelDescriptor Channel(string name, SampleType sampleType) => new() {
        Name = name,
        SampleType = sampleType,
        Sampling = SampleGrid.Unit,
    };

    private static SampleType SampleTypeForBitDepth(byte bitDepth) => bitDepth switch {
        1 => SampleType.UNorm1,
        2 => SampleType.UNorm2,
        4 => SampleType.UNorm4,
        8 => SampleType.UNorm8,
        16 => SampleType.UNorm16,
        _ => throw new ArgumentOutOfRangeException(nameof(bitDepth)),
    };
}
