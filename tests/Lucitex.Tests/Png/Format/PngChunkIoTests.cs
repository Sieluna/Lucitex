using Lucitex.Core.Execution;
using Lucitex.Png.Format;

namespace Lucitex.Tests.Png.Format;

public class PngChunkIoTests
{
    [Fact]
    public void WriteThenReadChunk_RoundTripsTypeAndData()
    {
        using var stream = new MemoryStream();
        PngChunkIo.WriteChunk(stream, "tEXt", "hello"u8);
        stream.Position = 0;

        var chunk = PngChunkIo.ReadChunk(stream);

        Assert.Equal("tEXt", chunk.Type);
        Assert.Equal("hello"u8.ToArray(), chunk.Data);
    }

    [Fact]
    public void ReadChunk_DetectsCorruptedCrc()
    {
        using var stream = new MemoryStream();
        PngChunkIo.WriteChunk(stream, "tEXt", "hello"u8);
        var bytes = stream.ToArray();
        bytes[^1] ^= 0xFF;

        using var corrupted = new MemoryStream(bytes);
        Assert.Throws<ImageFormatException>(() => PngChunkIo.ReadChunk(corrupted));
    }

    [Fact]
    public void ReadSignature_RejectsNonPngBytes()
    {
        using var stream = new MemoryStream([0, 1, 2, 3, 4, 5, 6, 7]);
        Assert.Throws<ImageFormatException>(() => PngChunkIo.ReadSignature(stream));
    }

    [Fact]
    public void WriteThenReadSignature_Succeeds()
    {
        using var stream = new MemoryStream();
        PngChunkIo.WriteSignature(stream);
        stream.Position = 0;

        PngChunkIo.ReadSignature(stream);
    }
}
