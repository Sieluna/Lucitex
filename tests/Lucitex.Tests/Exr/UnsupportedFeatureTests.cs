using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Tests.Exr;

public class UnsupportedFeatureTests
{
    private static ExrHeader BuildMinimalHeader(ExrCompressionId compression = ExrCompressionId.None) => new() {
        Channels = [new ExrChannelInfo { Name = "R", PixelType = ExrPixelType.Float }],
        Compression = compression,
        DataWindow = new ExrBox2i(0, 0, 3, 3),
        DisplayWindow = new ExrBox2i(0, 0, 3, 3),
    };

    private static WorkRegion FullRegion(long width, long height) => new() {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    private static MemoryStream WriteMinimalStream(ExrVersionFlags flags, ExrHeader header)
    {
        var stream = new MemoryStream();
        var writer = new ExrBinaryWriter(stream);
        ExrHeaderWriter.WriteFileVersion(writer, flags);
        ExrHeaderWriter.WriteHeader(writer, header);

        if (flags.HasFlag(ExrVersionFlags.MultiPart)) {
            writer.WriteByte(0);
        }

        var height = header.DataWindow.Height;
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(header.Compression);
        var chunkCount = (int)((height + linesPerChunk - 1) / linesPerChunk);

        var chunkDataStart = stream.Position + (chunkCount * 8L);
        for (var i = 0; i < chunkCount; i++) {
            writer.WriteInt64(chunkDataStart);
        }

        for (var i = 0; i < chunkCount; i++) {
            writer.WriteInt32(0);
            writer.WriteInt32(0);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void OpenReader_SucceedsForMultiPartFlag_DescriptorReportsAllParts()
    {
        var header = BuildMinimalHeader() with { PartName = "beauty", PartType = "scanlineimage" };
        using var stream = WriteMinimalStream(ExrVersionFlags.MultiPart, header);
        var codec = new Lucitex.Exr.ExrCodec();

        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        Assert.Single(described.Parts);
        Assert.Equal("beauty", described.Parts[0].Name);
    }

    [Fact]
    public void Read_RejectsDeepScanlinePart()
    {
        var header = BuildMinimalHeader() with { PartName = "deep", PartType = "deepscanline" };
        using var stream = WriteMinimalStream(ExrVersionFlags.MultiPart | ExrVersionFlags.NonImage, header);
        var codec = new Lucitex.Exr.ExrCodec();

        var reader = codec.OpenReader(stream);
        reader.Describe();

        var destination = new byte[4 * 4 * 4];
        var exception = Assert.Throws<ImageFormatException>(() => reader.Read(FullRegion(4, 4), destination));
        Assert.Contains("DeepData", exception.Code);
    }

    [Theory]
    [InlineData(ExrCompressionId.Pxr24)]
    [InlineData(ExrCompressionId.B44)]
    [InlineData(ExrCompressionId.B44A)]
    [InlineData(ExrCompressionId.Dwaa)]
    [InlineData(ExrCompressionId.Dwab)]
    public void Read_RejectsUnimplementedCompression_ButDescribeStillSucceeds(ExrCompressionId compression)
    {
        using var stream = WriteMinimalStream(ExrVersionFlags.None, BuildMinimalHeader(compression));
        var codec = new Lucitex.Exr.ExrCodec();

        var reader = codec.OpenReader(stream);
        var described = reader.Describe();
        Assert.Single(described.Parts);

        var destination = new byte[4 * 4 * 4];
        var exception = Assert.Throws<ImageFormatException>(() => reader.Read(FullRegion(4, 4), destination));
        Assert.Contains($"Unsupported.Exr.Compression.{compression}", exception.Code);
    }

    [Fact]
    public void CreateWriter_RejectsUnimplementedCompression()
    {
        var asset = Lucitex.Tests.Fixtures.ExrFixtures.SimpleRgba();
        var codec = new Lucitex.Exr.ExrCodec(ExrCompressionId.Pxr24);
        using var stream = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => codec.CreateWriter(stream, asset));
    }
}
