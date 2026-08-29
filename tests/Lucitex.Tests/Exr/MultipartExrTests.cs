using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Exr;

public class MultipartExrTests
{
    private static WorkRegion FullRegion(int partIndex, long width, long height) => new() {
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

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++) {
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

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++) {
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

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++) {
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

        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++) {
            var part = asset.Parts[partIndex];
            var width = part.Topology.BaseExtent.Width;
            var height = part.Topology.BaseExtent.Height;

            var destination = new byte[sources[partIndex].Length];
            reader.Read(FullRegion(partIndex, width, height), destination);

            Assert.Equal(sources[partIndex], destination);
        }
    }

    [Theory]
    [InlineData(ExrCompressionId.None)]
    [InlineData(ExrCompressionId.Zip)]
    [InlineData(ExrCompressionId.Piz)]
    public void Write_MultiPart_DeclaresChunkCountOnEveryPart(ExrCompressionId compression)
    {
        var asset = ExrFixtures.Multipart();
        var codec = new ExrCodec(compression);
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset);
        for (var partIndex = 0; partIndex < asset.Parts.Count; partIndex++) {
            var part = asset.Parts[partIndex];
            var width = part.Topology.BaseExtent.Width;
            var height = part.Topology.BaseExtent.Height;
            var header = ExrDescriptorMapper.ToExrHeader(part, compression);
            var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);

            writer.Write(FullRegion(partIndex, width, height), new byte[rowStride * height]);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = new ExrBinaryReader(stream, 1 << 20);
        ExrHeaderReader.ReadFileVersion(reader, out _);
        var headers = ExrHeaderReader.ReadHeaderList(reader, isMultiPart: true);

        Assert.Equal(asset.Parts.Count, headers.Count);

        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(compression);
        var expected = (int)((asset.Parts[0].Topology.BaseExtent.Height + linesPerChunk - 1) / linesPerChunk);

        foreach (var header in headers) {
            Assert.Equal(expected, header.ChunkCount);
            Assert.Equal("scanlineimage", header.PartType);
        }
    }

    [Fact]
    public void Write_SinglePart_OmitsChunkCount()
    {
        var asset = ExrFixtures.SimpleRgba();
        var codec = new ExrCodec(ExrCompressionId.Zip);
        using var stream = new MemoryStream();

        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var rowStride = ExrDescriptorMapper.ToExrHeader(part, ExrCompressionId.Zip).Channels.Sum(c => (int)width * c.BytesPerSample);

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(FullRegion(0, width, height), new byte[rowStride * height]);
        writer.Finish();

        stream.Position = 0;
        var reader = new ExrBinaryReader(stream, 1 << 20);
        ExrHeaderReader.ReadFileVersion(reader, out _);
        var headers = ExrHeaderReader.ReadHeaderList(reader, isMultiPart: false);

        Assert.Null(headers[0].ChunkCount);
    }
}
