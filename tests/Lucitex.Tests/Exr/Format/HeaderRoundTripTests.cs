using Lucitex.Exr.Format;

namespace Lucitex.Tests.Exr.Format;

public class HeaderRoundTripTests
{
    private static ExrHeader BuildHeader() => new() {
        Channels =
        [
            new ExrChannelInfo { Name = "A", PixelType = ExrPixelType.Half },
            new ExrChannelInfo { Name = "B", PixelType = ExrPixelType.Half },
            new ExrChannelInfo { Name = "G", PixelType = ExrPixelType.Half },
            new ExrChannelInfo { Name = "R", PixelType = ExrPixelType.Half },
        ],
        Compression = ExrCompressionId.Zip,
        DataWindow = new ExrBox2i(0, 0, 63, 31),
        DisplayWindow = new ExrBox2i(0, 0, 63, 31),
        LineOrder = ExrLineOrder.IncreasingY,
        PixelAspectRatio = 1.0f,
        PartName = "beauty",
        UnknownAttributes =
        [
            new ExrRawAttribute { Name = "owner", Type = "string", Value = "lucitex"u8.ToArray() },
        ],
    };

    [Fact]
    public void WriteThenRead_PreservesAllHeaderFields()
    {
        var header = BuildHeader();

        using var stream = new MemoryStream();
        var writer = new ExrBinaryWriter(stream);
        ExrHeaderWriter.WriteFileVersion(writer, ExrVersionFlags.None);
        ExrHeaderWriter.WriteHeader(writer, header);

        stream.Position = 0;
        var reader = new ExrBinaryReader(stream);
        var flags = ExrHeaderReader.ReadFileVersion(reader, out var version);
        var roundTripped = ExrHeaderReader.ReadHeader(reader);

        Assert.Equal(ExrVersionFlags.None, flags);
        Assert.Equal(2, version);

        Assert.Equal(header.Channels.Count, roundTripped.Channels.Count);
        for (var i = 0; i < header.Channels.Count; i++) {
            Assert.Equal(header.Channels[i].Name, roundTripped.Channels[i].Name);
            Assert.Equal(header.Channels[i].PixelType, roundTripped.Channels[i].PixelType);
            Assert.Equal(header.Channels[i].XSampling, roundTripped.Channels[i].XSampling);
            Assert.Equal(header.Channels[i].YSampling, roundTripped.Channels[i].YSampling);
        }

        Assert.Equal(header.Compression, roundTripped.Compression);
        Assert.Equal(header.DataWindow, roundTripped.DataWindow);
        Assert.Equal(header.DisplayWindow, roundTripped.DisplayWindow);
        Assert.Equal(header.LineOrder, roundTripped.LineOrder);
        Assert.Equal(header.PixelAspectRatio, roundTripped.PixelAspectRatio);
        Assert.Equal(header.PartName, roundTripped.PartName);

        var owner = Assert.Single(roundTripped.UnknownAttributes);
        Assert.Equal("owner", owner.Name);
        Assert.Equal("string", owner.Type);
        Assert.Equal("lucitex"u8.ToArray(), owner.Value);
    }

    [Fact]
    public void ReadFileVersion_RejectsBadMagic()
    {
        using var stream = new MemoryStream([0, 0, 0, 0, 2, 0, 0, 0]);
        var reader = new ExrBinaryReader(stream);

        Assert.Throws<Lucitex.Core.Execution.ImageFormatException>(() => ExrHeaderReader.ReadFileVersion(reader, out _));
    }

    [Fact]
    public void ReadHeader_DecodesTiledFlagAndTileDescriptor()
    {
        var header = BuildHeader() with {
            Tiles = new ExrTileDesc(64, 64, ExrTileLevelMode.MipmapLevels, ExrTileRoundingMode.RoundUp),
        };

        using var stream = new MemoryStream();
        var writer = new ExrBinaryWriter(stream);
        ExrHeaderWriter.WriteFileVersion(writer, ExrVersionFlags.Tiled);
        ExrHeaderWriter.WriteHeader(writer, header);

        stream.Position = 0;
        var reader = new ExrBinaryReader(stream);
        var flags = ExrHeaderReader.ReadFileVersion(reader, out _);
        var roundTripped = ExrHeaderReader.ReadHeader(reader);

        Assert.Equal(ExrVersionFlags.Tiled, flags);
        Assert.True(roundTripped.Tiles.HasValue);
        var tiles = roundTripped.Tiles!.Value;
        Assert.Equal(64u, tiles.XSize);
        Assert.Equal(ExrTileLevelMode.MipmapLevels, tiles.LevelMode);
        Assert.Equal(ExrTileRoundingMode.RoundUp, tiles.RoundingMode);
    }
}
