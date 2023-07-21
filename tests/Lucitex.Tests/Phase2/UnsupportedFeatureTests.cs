using Lucitex.Core.Execution;
using Lucitex.Exr.Format;

namespace Lucitex.Tests.Phase2;

public class UnsupportedFeatureTests
{
    private static ExrHeader BuildMinimalHeader(ExrCompressionId compression = ExrCompressionId.None) => new()
    {
        Channels = [new ExrChannelInfo { Name = "R", PixelType = ExrPixelType.Float }],
        Compression = compression,
        DataWindow = new ExrBox2i(0, 0, 3, 3),
        DisplayWindow = new ExrBox2i(0, 0, 3, 3),
    };

    private static MemoryStream WriteMinimalStream(ExrVersionFlags flags, ExrHeader header)
    {
        var stream = new MemoryStream();
        var writer = new ExrBinaryWriter(stream);
        ExrHeaderWriter.WriteFileVersion(writer, flags);
        ExrHeaderWriter.WriteHeader(writer, header);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void OpenReader_RejectsTiledFlag()
    {
        using var stream = WriteMinimalStream(ExrVersionFlags.Tiled, BuildMinimalHeader());
        var codec = new Lucitex.Exr.ExrCodec();

        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(stream));
        Assert.Contains("Tiled", exception.Code);
    }

    [Fact]
    public void OpenReader_RejectsMultiPartFlag()
    {
        using var stream = WriteMinimalStream(ExrVersionFlags.MultiPart, BuildMinimalHeader());
        var codec = new Lucitex.Exr.ExrCodec();

        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(stream));
        Assert.Contains("MultiPart", exception.Code);
    }

    [Fact]
    public void OpenReader_RejectsNonImageDeepFlag()
    {
        using var stream = WriteMinimalStream(ExrVersionFlags.NonImage, BuildMinimalHeader());
        var codec = new Lucitex.Exr.ExrCodec();

        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(stream));
        Assert.Contains("DeepData", exception.Code);
    }

    [Theory]
    [InlineData(ExrCompressionId.Piz)]
    [InlineData(ExrCompressionId.Pxr24)]
    [InlineData(ExrCompressionId.B44)]
    [InlineData(ExrCompressionId.B44A)]
    [InlineData(ExrCompressionId.Dwaa)]
    [InlineData(ExrCompressionId.Dwab)]
    public void OpenReader_RejectsUnimplementedCompressionButStillParsesHeader(ExrCompressionId compression)
    {
        using var stream = WriteMinimalStream(ExrVersionFlags.None, BuildMinimalHeader(compression));
        var codec = new Lucitex.Exr.ExrCodec();

        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(stream));
        Assert.Contains($"Unsupported.Exr.Compression.{compression}", exception.Code);
    }

    [Fact]
    public void CreateWriter_RejectsUnimplementedCompression()
    {
        var asset = Lucitex.Tests.Fixtures.ExrFixtures.SimpleRgba();
        var codec = new Lucitex.Exr.ExrCodec(ExrCompressionId.Piz);
        using var stream = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => codec.CreateWriter(stream, asset));
    }
}
