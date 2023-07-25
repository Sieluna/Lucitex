using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Phase2;

public class MultipartExrTests
{
    private static WorkRegion FullRegion(int partIndex, long width, long height) => new()
    {
        Subresource = new SubresourceId(partIndex, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    [Theory]
    [InlineData(ExrCompressionId.None)]
    [InlineData(ExrCompressionId.Zip)]
    public void RoundTrip_TwoParts_KeepsEachPartsPixelsIndependent(ExrCompressionId compression)
    {
        var asset = ExrFixtures.Multipart();
        Assert.Equal(2, asset.Parts.Count);

        var codec = new ExrCodec(compression);
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var sources = new byte[asset.Parts.Count][];
        var seed = 1;

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++)
        {
            var part = asset.Parts[partIndex];
            var width = part.Topology.BaseExtent.Width;
            var height = part.Topology.BaseExtent.Height;
            var header = ExrDescriptorMapper.ToExrHeader(part, compression);
            var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);

            var source = new byte[rowStride * height];
            new Random(seed++).NextBytes(source);
            sources[partIndex] = source;

            writer.Write(FullRegion(partIndex, width, height), source);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        Assert.Equal(2, described.Parts.Count);
        Assert.Equal("diffuse", described.Parts[0].Name);
        Assert.Equal("specular", described.Parts[1].Name);

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++)
        {
            var part = asset.Parts[partIndex];
            var width = part.Topology.BaseExtent.Width;
            var height = part.Topology.BaseExtent.Height;

            var destination = new byte[sources[partIndex].Length];
            var readCount = reader.Read(FullRegion(partIndex, width, height), destination);

            Assert.Equal(sources[partIndex].Length, readCount);
            Assert.Equal(sources[partIndex], destination);
        }
    }

    [Fact]
    public void RoundTrip_TwoParts_TiledOneLevel_KeepsEachPartsPixelsIndependent()
    {
        var asset = ExrFixtures.Multipart();
        var tiles = new ExrTileDesc(3, 3, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown);
        var codec = new ExrCodec(ExrCompressionId.Zips, tiles);

        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var sources = new byte[asset.Parts.Count][];
        var seed = 11;

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++)
        {
            var part = asset.Parts[partIndex];
            var width = part.Topology.BaseExtent.Width;
            var height = part.Topology.BaseExtent.Height;
            var header = ExrDescriptorMapper.ToExrHeader(part, ExrCompressionId.Zips);
            var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);

            var source = new byte[rowStride * height];
            new Random(seed++).NextBytes(source);
            sources[partIndex] = source;

            writer.Write(FullRegion(partIndex, width, height), source);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++)
        {
            var part = asset.Parts[partIndex];
            var width = part.Topology.BaseExtent.Width;
            var height = part.Topology.BaseExtent.Height;

            var destination = new byte[sources[partIndex].Length];
            reader.Read(FullRegion(partIndex, width, height), destination);

            Assert.Equal(sources[partIndex], destination);
        }
    }
}
