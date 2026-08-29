using Lucitex.Core.Execution;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Exr;

public class TiledExrTests
{
    private static WorkRegion FullRegion(long width, long height) => new() {
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


    private static WorkRegion LevelRegion(LevelKey level, long minX, long minY, long width, long height) => new() {
        Subresource = new SubresourceId(0, 0, 0, level),
        Region = ImageBox.FromExclusive(minX, minY, minX + width, minY + height),
    };

    private static Dictionary<LevelKey, byte[]> RoundTripAllLevels(
        ImageAssetDescriptor asset,
        ExrTileDesc tiles,
        ExrCompressionId compression,
        out ImageAssetDescriptor described)
    {
        var window = asset.Parts[0].Spatial.DataWindow;
        var header = ExrDescriptorMapper.ToExrHeader(asset, compression);
        var bytesPerPixel = header.Channels.Sum(c => c.BytesPerSample);

        var levels = ExrTiling.Levels(tiles, window.Width, window.Height);
        var sources = new Dictionary<LevelKey, byte[]>();
        var random = new Random(9182);

        var codec = new ExrCodec(compression, tiles);
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        foreach (var level in levels) {
            var key = ExrDescriptorMapper.ToLevelKey(tiles.LevelMode, level);
            var source = new byte[level.Width * level.Height * bytesPerPixel];
            random.NextBytes(source);
            sources[key] = source;

            writer.Write(LevelRegion(key, window.MinX, window.MinY, level.Width, level.Height), source);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        described = reader.Describe();

        foreach (var level in levels) {
            var key = ExrDescriptorMapper.ToLevelKey(tiles.LevelMode, level);
            var destination = new byte[sources[key].Length];
            var read = reader.Read(LevelRegion(key, window.MinX, window.MinY, level.Width, level.Height), destination);

            Assert.Equal(sources[key].Length, read);
            Assert.Equal(sources[key], destination);
        }

        return sources;
    }

    [Theory]
    [InlineData(ExrCompressionId.None, ExrTileRoundingMode.RoundDown)]
    [InlineData(ExrCompressionId.Zip, ExrTileRoundingMode.RoundDown)]
    [InlineData(ExrCompressionId.Rle, ExrTileRoundingMode.RoundUp)]
    [InlineData(ExrCompressionId.Zip, ExrTileRoundingMode.RoundUp)]
    public void RoundTrip_MipmapTiled_PreservesEveryLevel(ExrCompressionId compression, ExrTileRoundingMode rounding)
    {
        var asset = ExrFixtures.SimpleRgba();
        var tiles = new ExrTileDesc(16, 16, ExrTileLevelMode.MipmapLevels, rounding);

        RoundTripAllLevels(asset, tiles, compression, out var described);

        var levels = described.Parts[0].Topology.Levels;
        Assert.Equal(7, levels.Count);
        Assert.Equal(new Extent3L(64, 32, 1), levels[0].Extent);
        Assert.Equal(new Extent3L(32, 16, 1), levels[1].Extent);
        Assert.Equal(new Extent3L(1, 1, 1), levels[6].Extent);
        Assert.Equal(LevelKey.Mip(3), levels[3].Key);
    }

    [Theory]
    [InlineData(ExrCompressionId.None)]
    [InlineData(ExrCompressionId.Zip)]
    public void RoundTrip_RipmapTiled_PreservesEveryLevel(ExrCompressionId compression)
    {
        var asset = ExrFixtures.SimpleRgba();
        var tiles = new ExrTileDesc(8, 8, ExrTileLevelMode.RipmapLevels, ExrTileRoundingMode.RoundDown);

        RoundTripAllLevels(asset, tiles, compression, out var described);

        var levels = described.Parts[0].Topology.Levels;
        Assert.Equal(7 * 6, levels.Count);

        var byKey = levels.ToDictionary(level => level.Key, level => level.Extent);
        Assert.Equal(new Extent3L(64, 32, 1), byKey[new LevelKey(0, 0, 0)]);
        Assert.Equal(new Extent3L(16, 32, 1), byKey[new LevelKey(2, 0, 0)]);
        Assert.Equal(new Extent3L(64, 4, 1), byKey[new LevelKey(0, 3, 0)]);
        Assert.Equal(new Extent3L(1, 1, 1), byKey[new LevelKey(6, 5, 0)]);
    }

    [Fact]
    public void RoundTrip_MipmapTiled_WithRoundUpAndOddExtent_PreservesEveryLevel()
    {
        var asset = ExrFixtures.NegativeDataWindow();
        var tiles = new ExrTileDesc(4, 4, ExrTileLevelMode.MipmapLevels, ExrTileRoundingMode.RoundUp);

        RoundTripAllLevels(asset, tiles, ExrCompressionId.Zip, out var described);

        var window = asset.Parts[0].Spatial.DataWindow;
        var levels = described.Parts[0].Topology.Levels;

        Assert.Equal(new Extent3L(window.Width, window.Height, 1), levels[0].Extent);
        Assert.Equal(new Extent3L(1, 1, 1), levels[^1].Extent);
    }

    [Fact]
    public void Read_RejectsLevelThatDoesNotExist()
    {
        var asset = ExrFixtures.SimpleRgba();
        var tiles = new ExrTileDesc(16, 16, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown);
        var codec = new ExrCodec(ExrCompressionId.Zip, tiles);
        using var stream = new MemoryStream();

        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Zip);
        var bytesPerPixel = header.Channels.Sum(c => c.BytesPerSample);

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(FullRegion(64, 32), new byte[64 * 32 * bytesPerPixel]);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.Read(LevelRegion(LevelKey.Mip(1), 0, 0, 32, 16), new byte[32 * 16 * bytesPerPixel]));
    }
}
