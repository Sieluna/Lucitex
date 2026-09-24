using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Ktx2;

namespace Lucitex.Fuzz;

internal static partial class SeedCorpus
{
    private static IEnumerable<SeedInput> CreateTopologySeeds()
    {
        var texture = PngDescriptor(8, 8, ["R", "G", "B", "A"], SampleType.UNorm8);
        var part = texture.Parts[0];
        var mipmaps = Enumerable.Range(0, 4).Select(mip => new ResolutionLevel {
            Key = LevelKey.Mip(mip), Extent = new Extent3L(8 >> mip, 8 >> mip, 1),
        }).ToArray();
        texture = texture with { Parts = [part with {
            Topology = part.Topology with { Levels = mipmaps, ArrayElementCount = 2, FaceCount = 6 },
        }] };
        yield return new("mip-cube-array.ktx2", ImageFormat.Ktx2, WriteSubresources(new Ktx2Codec(), texture, 201));

        texture = texture with { Parts = [part with {
            Topology = part.Topology with {
                SpatialDimensions = 3,
                BaseExtent = new Extent3L(8, 8, 4),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(8, 8, 4) }],
            },
        }] };
        yield return new("volume.ktx2", ImageFormat.Ktx2, WriteSubresources(new Ktx2Codec(), texture, 202));

        var exr = PngDescriptor(12, 8, ["R", "G", "B", "A"], SampleType.Float16);
        yield return new("rgba-piz.exr", ImageFormat.Exr, WriteSubresources(new ExrCodec(ExrCompressionId.Piz), exr, 203));
        yield return new("rgba-zips.exr", ImageFormat.Exr, WriteSubresources(new ExrCodec(ExrCompressionId.Zips), exr, 204));
        var exrPart = exr.Parts[0];
        var offset = ImageBox.FromExclusive(-4, 6, 8, 14);
        var shifted = exr with { Parts = [exrPart with {
            Spatial = exrPart.Spatial with { DataWindow = offset, DisplayWindow = offset },
        }] };
        yield return new("offset.exr", ImageFormat.Exr, WriteSubresources(new ExrCodec(), shifted, 205));

        var subsampled = Asset(12, 8,
            [new ChannelDescriptor { Name = "Y", SampleType = SampleType.Float16, Sampling = new SampleGrid { Origin = Long3.Zero, Step = new Int3(2, 2, 1) } }],
            new PlainSampleRepresentation { Planes = [new SamplePlaneDescriptor { Channels = ["Y"], Extent = new Extent3L(6, 4, 1), Layout = PlaneLayout.Planar }] });
        yield return new("subsampled.exr", ImageFormat.Exr, WriteSubresources(new ExrCodec(), subsampled, 206));

        var multipart = exr with { Parts = [exrPart with { Name = "left" }, exrPart with { Name = "right" }] };
        yield return new("multipart.exr", ImageFormat.Exr, WriteSubresources(new ExrCodec(), multipart, 207));

        var tiled = exr with { Parts = [exrPart with {
            Topology = exrPart.Topology with { Levels = Enumerable.Range(0, 4).Select(mip => new ResolutionLevel {
                Key = LevelKey.Mip(mip), Extent = new Extent3L(Math.Max(1, 12 >> mip), Math.Max(1, 8 >> mip), 1),
            }).ToArray() },
        }] };
        var tiles = new ExrTileDesc(4, 4, ExrTileLevelMode.MipmapLevels, ExrTileRoundingMode.RoundDown);
        yield return new("tiled-mips.exr", ImageFormat.Exr, WriteSubresources(new ExrCodec(ExrCompressionId.Zip, tiles), tiled, 208));
    }

    private static byte[] WriteSubresources(IImageCodec codec, ImageAssetDescriptor descriptor, int randomSeed)
    {
        using var stream = new MemoryStream();
        using var writer = codec.CreateWriter(stream, descriptor);
        var random = new Random(randomSeed);
        foreach (var read in SubresourceLayout.Enumerate(descriptor, FuzzLimits.Decode)) {
            var pixels = new byte[read.ByteCount];
            random.NextBytes(pixels);
            writer.Write(read.Region, pixels);
        }
        writer.Finish();
        return stream.ToArray();
    }
}
