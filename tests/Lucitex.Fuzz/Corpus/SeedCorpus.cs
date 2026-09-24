using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Hdr;
using Lucitex.Jpeg;
using Lucitex.Ktx2;
using Lucitex.Png;
using Lucitex.Webp;

namespace Lucitex.Fuzz;

internal readonly record struct SeedInput(string Name, ImageFormat Format, byte[] Bytes);

internal static partial class SeedCorpus
{
    public static IReadOnlyList<SeedInput> Create()
    {
        var seeds = new List<SeedInput>
        {
            new("rgba8.png", ImageFormat.Png, WritePng(PngDescriptor(17, 13, ["R", "G", "B", "A"], SampleType.UNorm8), 17 * 13 * 4, 1)),
            new("gray1.png", ImageFormat.Png, WritePng(PngDescriptor(19, 11, ["Y"], SampleType.UNorm1), ((19 + 7) / 8) * 11, 2)),
            new("rgb16.png", ImageFormat.Png, WritePng(PngDescriptor(9, 7, ["R", "G", "B"], SampleType.UNorm16), 9 * 7 * 6, 3)),
            new("palette4.png", ImageFormat.Png, WritePng(PaletteDescriptor(), ((15 + 1) / 2) * 9, 4)),
            new("rgba-none.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.None, 5)),
            new("rgba-rle.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.Rle, 6)),
            new("rgba-zip.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.Zip, 7)),
            new("rgbe.hdr", ImageFormat.Hdr, WriteHdr(31, 12, 10)),
            new("rgb8.jpg", ImageFormat.Jpeg, WriteJpegRgb8(24, 18, 14)),
            new("gray8.jpg", ImageFormat.Jpeg, WriteJpegGray8(20, 16, 15)),
            new("rgba8.webp", ImageFormat.Webp, WriteWebpRgba8(17, 13, 16)),
            new("lossy-alpha.webp", ImageFormat.Webp, s_WebpLossyAlphaSeed),
            new("rgba8.ktx2", ImageFormat.Ktx2, WriteKtx2Rgba8(12, 9, 8)),
            new("r32f.ktx2", ImageFormat.Ktx2, WriteKtx2R32Float(11, 6, 9)),
            new("r10g10b10a2.ktx2", ImageFormat.Ktx2, WriteKtx2Packed(EncodedFormatId.R10G10B10A2, 13, 7, 11)),
            new("r11g11b10.ktx2", ImageFormat.Ktx2, WriteKtx2Packed(EncodedFormatId.R11G11B10Float, 13, 7, 12)),
            new("rgb9e5.ktx2", ImageFormat.Ktx2, WriteKtx2Packed(EncodedFormatId.Rgb9E5, 13, 7, 13)),
        };
        seeds.AddRange(CreateTopologySeeds());
        foreach (var effort in Enum.GetValues<WebpCompressionEffort>()) {
            foreach (var quality in new[] { 0, 75, 100 }) {
                var options = new WebpEncoderOptions { Lossless = false, Quality = quality, Effort = effort };
                seeds.Add(new($"managed-lossy-{quality}-{effort}.webp", ImageFormat.Webp,
                    WriteWebpRgba8(quality == 0 ? 1 : 17, quality == 100 ? 1 : 19, 17, options)));
            }
        }
        return seeds;
    }

    public static IEnumerable<SeedInput> CreateBenchmark(int width, int height)
    {
        var rgbaChannels = new[] { "R", "G", "B", "A" };
        yield return new SeedInput("rgba8.png", ImageFormat.Png, WritePng(PngDescriptor(width, height, rgbaChannels, SampleType.UNorm8), checked(width * height * 4), 101));
        yield return new SeedInput("rgba-zip.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.Zip, width, height, 102));
        yield return new SeedInput("rgba8.ktx2", ImageFormat.Ktx2, WriteKtx2Rgba8(width, height, 103));
        yield return new SeedInput("rgb8.jpg", ImageFormat.Jpeg, WriteJpegRgb8(width, height, 104));
    }

    private static ImageAssetDescriptor PngDescriptor(int width, int height, IReadOnlyList<string> names, SampleType sampleType)
    {
        var channels = names.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = sampleType,
            Sampling = SampleGrid.Unit,
        }).ToList();
        return Asset(width, height, channels, new PlainSampleRepresentation {
            Planes =
            [
                new SamplePlaneDescriptor
                {
                    Channels = names.Select(name => (ChannelPath)name).ToList(),
                    Extent = new Extent3L(width, height, 1),
                    Layout = PlaneLayout.Interleaved,
                },
            ],
        });
    }

    private static ImageAssetDescriptor PaletteDescriptor()
    {
        var descriptor = Asset(15, 9,
        [
            new ChannelDescriptor { Name = "Index", SampleType = SampleType.UNorm4, Sampling = SampleGrid.Unit },
        ],
        new IndexedRepresentation {
            IndexType = SampleType.UNorm4,
            Palette = new PaletteDescriptor {
                EntryCount = 16,
                EntryChannels = new ChannelSchema {
                    Channels =
                    [
                        new ChannelDescriptor { Name = "R", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
                        new ChannelDescriptor { Name = "G", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
                        new ChannelDescriptor { Name = "B", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
                    ],
                },
                EntrySampleType = SampleType.UNorm8,
            },
        });

        var palette = Enumerable.Range(0, 16)
            .SelectMany(value => new[] { (byte)(value * 17), (byte)(255 - (value * 17)), (byte)(value * 7) })
            .ToArray();
        var part = descriptor.Parts[0] with {
            Metadata = new MetadataCollection {
                Entries = [new MetadataEntry { Namespace = "png", Name = "PLTE", RawRepresentation = palette }],
            },
        };
        return descriptor with { Parts = [part] };
    }

    private static byte[] WritePng(ImageAssetDescriptor descriptor, int byteCount, int randomSeed) =>
        Write(new PngCodec(), descriptor, byteCount, randomSeed);

    private static byte[] WriteJpegRgb8(int width, int height, int randomSeed)
    {
        var channels = new[] { "R", "G", "B" }.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = SampleType.UNorm8,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes = [new SamplePlaneDescriptor { Channels = ["R", "G", "B"], Extent = new Extent3L(width, height, 1), Layout = PlaneLayout.Interleaved }],
        });
        return Write(new JpegCodec(), descriptor, width * height * 3, randomSeed);
    }

    private static byte[] WriteJpegGray8(int width, int height, int randomSeed)
    {
        var channels = new List<ChannelDescriptor> {
            new() { Name = "Y", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
        };
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes = [new SamplePlaneDescriptor { Channels = ["Y"], Extent = new Extent3L(width, height, 1), Layout = PlaneLayout.Interleaved }],
        });
        return Write(new JpegCodec(), descriptor, width * height, randomSeed);
    }

    private static byte[] WriteExr(ExrCompressionId compression, int randomSeed)
    {
        const int width = 13;
        const int height = 10;
        return WriteExr(compression, width, height, randomSeed);
    }

    private static byte[] WriteExr(ExrCompressionId compression, int width, int height, int randomSeed)
    {
        var channels = new[] { "R", "G", "B", "A" }.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = SampleType.Float16,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes = channels.Select(channel => new SamplePlaneDescriptor {
                Channels = [channel.Name],
                Extent = new Extent3L(width, height, 1),
                Layout = PlaneLayout.Planar,
            }).ToList(),
        });
        return Write(new ExrCodec(compression), descriptor, width * height * channels.Count * 2, randomSeed);
    }

    private static readonly byte[] s_WebpLossyAlphaSeed = Convert.FromBase64String(
        "UklGRtoBAABXRUJQVlA4WAoAAAAQAAAAHwAAHwAAQUxQSBgAAAABuYzof4BI22Zs9+96+CRiAiaAqJXNfQFWUDggnAEAABAJAJ0BKiAAIAA+bS6URyQioiEoCqiADYlsPF+jAsMUIovV1FEAfwCkNESxv6Bbh2gnFvp/5v+5AP//pJf9L/s33//ix///T/9AD9/wvfY/AAD+5k/YNPF3vY/+pP/tXaEgD3OEGlNFaI9iaqzs6do49GWH+Fo3y6v9+ienQdBMm2OwY6KBSn5jJujlF/IzFbw4px/9e+L+lIXjwaP6+wcT7Mkhp/lkS34UhZ/tav7jm9XsMqeL4WsaPWf2uiqJkU/DTJCiPxeWeK7InOyvVOwhfK3H8bf9U62Ol0PuKoTdGcTJBKbdkq5nIoBL3pNQCQi7fkLfSS+wq0pbsv4QWDsEemlZspIwUs/iJT+7ln/MDJDnGnUxJg76eIN9QNYDwWc/ivr1nHmJcAJBGjAUhZ//hFRn+i5zyENYCVVfljZhul55JX8ZMxnYhWYLsMqgzHsLJTTfHGVF5sxtWoV3hSkGhyphPCYqSkgmGSYaP4rgP19bQK8Nd/qSpPsQSz9e9XuDEFI8D6Tpnl5CMBNZKN7zF6/1wp8uPtKwAAA=");

    private static byte[] WriteWebpRgba8(int width, int height, int randomSeed, WebpEncoderOptions? options = null)
    {
        string[] names = ["R", "G", "B", "A"];
        var channels = names.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = SampleType.UNorm8,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var window = ImageBox.FromOrigin(width, height);
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain {
                DataWindow = window,
                DisplayWindow = window,
                Orientation = LogicalOrientation.Identity,
                Traversal = StorageTraversal.IncreasingY,
            },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = channels },
            Representation = new PlainSampleRepresentation {
                Planes = [new SamplePlaneDescriptor { Channels = names.Select(name => (ChannelPath)name).ToList(), Extent = new Extent3L(width, height, 1), Layout = PlaneLayout.Interleaved }],
            },
            Alpha = new AlphaDescriptor { Mode = AlphaMode.Straight },
            Color = new ColorEncoding { Transfer = TransferFunction.Srgb },
        };
        var descriptor = new ImageAssetDescriptor { Parts = [part] };
        var codec = new WebpCodec();
        return Write(codec, descriptor, width * height * 4, randomSeed,
            options is null ? null : stream => codec.CreateWriter(stream, descriptor, options));
    }

    private static byte[] WriteKtx2Rgba8(int width, int height, int randomSeed)
    {
        var channels = new[] { "R", "G", "B", "A" }.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = SampleType.UNorm8,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes =
            [
                new SamplePlaneDescriptor
                {
                    Channels = channels.Select(channel => (ChannelPath)channel.Name).ToList(),
                    Extent = new Extent3L(width, height, 1),
                    Layout = PlaneLayout.Interleaved,
                },
            ],
        });
        return Write(new Ktx2Codec(), descriptor, width * height * 4, randomSeed);
    }

    private static byte[] WriteKtx2R32Float(int width, int height, int randomSeed)
    {
        var channels = new List<ChannelDescriptor>
        {
            new() { Name = "R", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
        };
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1), Layout = PlaneLayout.Interleaved }],
        });
        return Write(new Ktx2Codec(), descriptor, width * height * 4, randomSeed);
    }

    private static byte[] WriteKtx2Packed(EncodedFormatId format, int width, int height, int randomSeed)
    {
        var channelNames = format == EncodedFormatId.R10G10B10A2 ? new[] { "R", "G", "B", "A" } : ["R", "G", "B"];
        var sampleType = format == EncodedFormatId.R10G10B10A2 ? SampleType.UNorm16 : SampleType.Float16;
        var channels = channelNames.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = name == "A" ? SampleType.UNorm8 : sampleType,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var fields = format.Name switch {
            nameof(EncodedFormatId.R10G10B10A2) => new[] { new PackedField("R", 0, 10), new PackedField("G", 10, 10), new PackedField("B", 20, 10), new PackedField("A", 30, 2) },
            nameof(EncodedFormatId.R11G11B10Float) => [new PackedField("R", 0, 11), new PackedField("G", 11, 11), new PackedField("B", 22, 10)],
            _ => [new PackedField("R", 0, 9), new PackedField("G", 9, 9), new PackedField("B", 18, 9), new PackedField("E", 27, 5)],
        };
        var descriptor = Asset(width, height, channels, new EncodedElementRepresentation {
            Format = format,
            TexelExtentPerElement = new Extent3I(1, 1, 1),
            BitsPerElement = 32,
            Class = format == EncodedFormatId.Rgb9E5 ? EncodedElementClass.SharedExponent : EncodedElementClass.Packed,
            PackedLayout = new PackedFieldLayout { Fields = fields },
        });
        return Write(new Ktx2Codec(), descriptor, width * height * 4, randomSeed);
    }

    private static byte[] WriteHdr(int width, int height, int randomSeed)
    {
        var channels = new List<ChannelDescriptor>
        {
            new() { Name = "R", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
            new() { Name = "G", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
            new() { Name = "B", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
        };
        var descriptor = Asset(width, height, channels, new EncodedElementRepresentation {
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
        });
        return Write(new HdrCodec(), descriptor, width * height * 4, randomSeed);
    }

    private static ImageAssetDescriptor Asset(
        int width,
        int height,
        IReadOnlyList<ChannelDescriptor> channels,
        PayloadRepresentation representation)
    {
        var window = ImageBox.FromOrigin(width, height);
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain {
                DataWindow = window,
                DisplayWindow = window,
                Orientation = LogicalOrientation.Identity,
                Traversal = StorageTraversal.IncreasingY,
            },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = channels },
            Representation = representation,
            Color = new ColorEncoding { Transfer = TransferFunction.Linear },
        };
        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static byte[] Write(IImageCodec codec, ImageAssetDescriptor descriptor, int byteCount, int randomSeed,
        Func<Stream, IImageWriter>? createWriter = null)
    {
        var pixels = new byte[byteCount];
        new Random(randomSeed).NextBytes(pixels);
        using var stream = new MemoryStream();
        using var writer = createWriter?.Invoke(stream) ?? codec.CreateWriter(stream, descriptor);
        writer.Write(new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = descriptor.Parts[0].Spatial.DataWindow,
        }, pixels);
        writer.Finish();
        return stream.ToArray();
    }
}
