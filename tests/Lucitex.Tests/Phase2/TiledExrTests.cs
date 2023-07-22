using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Phase2;

public class TiledExrTests
{
    private static WorkRegion FullRegion(long width, long height) => new()
    {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    [Theory]
    [InlineData(16u, 16u, ExrCompressionId.None)]
    [InlineData(16u, 16u, ExrCompressionId.Zip)]
    [InlineData(9u, 7u, ExrCompressionId.Rle)]
    [InlineData(9u, 7u, ExrCompressionId.Zips)]
    [InlineData(100u, 100u, ExrCompressionId.Zip)]
    public void RoundTrip_TiledOneLevel_PreservesPixelsAndDescriptor(uint tileX, uint tileY, ExrCompressionId compression)
    {
        var asset = ExrFixtures.SimpleRgba();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var header = ExrDescriptorMapper.ToExrHeader(asset, compression);
        var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);
        var source = new byte[rowStride * height];
        new Random(123).NextBytes(source);

        var tiles = new ExrTileDesc(tileX, tileY, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown);
        var codec = new ExrCodec(compression, tiles);
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(FullRegion(width, height), source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        Assert.Equal(width, described.Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(height, described.Parts[0].Topology.BaseExtent.Height);

        var destination = new byte[source.Length];
        var readCount = reader.Read(FullRegion(width, height), destination);

        Assert.Equal(source.Length, readCount);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void OpenReader_RejectsMipmapTiledFiles()
    {
        var header = new ExrHeader
        {
            Channels = [new ExrChannelInfo { Name = "R", PixelType = ExrPixelType.Float }],
            Compression = ExrCompressionId.None,
            DataWindow = new ExrBox2i(0, 0, 15, 15),
            DisplayWindow = new ExrBox2i(0, 0, 15, 15),
            Tiles = new ExrTileDesc(8, 8, ExrTileLevelMode.MipmapLevels, ExrTileRoundingMode.RoundDown),
        };

        using var stream = new MemoryStream();
        var writer = new ExrBinaryWriter(stream);
        ExrHeaderWriter.WriteFileVersion(writer, ExrVersionFlags.Tiled);
        ExrHeaderWriter.WriteHeader(writer, header);
        stream.Position = 0;

        var codec = new ExrCodec();
        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(stream));
        Assert.Contains("MipRipmapTiles", exception.Code);
    }
}
