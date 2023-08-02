using Lucitex.Core.Execution.Buffers;

namespace Lucitex.Tests.Core.Execution.Buffers;

public class LargeBufferTests
{
    private static byte[] MakeData(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 256);
        }

        return data;
    }

    [Fact]
    public void PooledSegmentedBuffer_ReadsBackExactBytesAcrossChunkBoundary()
    {
        const int chunkSize = 1 << 20;
        var data = MakeData(chunkSize + 4096);

        using var buffer = new PooledSegmentedBuffer();
        buffer.Append(data);

        Assert.Equal(data.Length, buffer.Length);

        var segment = buffer.GetSegment(chunkSize - 100, 200);

        Assert.True(data.AsSpan(chunkSize - 100, 200).SequenceEqual(segment.Memory.Span));
    }

    [Fact]
    public void PooledSegmentedBuffer_ReadsBackExactBytesWithinSingleChunk()
    {
        var data = MakeData(4096);

        using var buffer = new PooledSegmentedBuffer();
        buffer.Append(data);

        var segment = buffer.GetSegment(10, 100);

        Assert.True(data.AsSpan(10, 100).SequenceEqual(segment.Memory.Span));
    }

    [Fact]
    public void PooledSegmentedBuffer_ThrowsWhenSegmentExceedsLength()
    {
        using var buffer = new PooledSegmentedBuffer();
        buffer.Append(MakeData(10));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetSegment(5, 10));
    }

    [Fact]
    public void PooledSegmentedBuffer_ThrowsAfterDispose()
    {
        var buffer = new PooledSegmentedBuffer();
        buffer.Append(MakeData(10));
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.GetSegment(0, 5));
    }

    [Fact]
    public void StreamBackedBuffer_ReadsBackExactBytes()
    {
        var data = MakeData(8192);
        using var stream = new MemoryStream(data);
        ILargeBuffer buffer = new StreamBackedBuffer(stream);

        Assert.Equal(data.Length, buffer.Length);

        var segment = buffer.GetSegment(100, 500);

        Assert.True(data.AsSpan(100, 500).SequenceEqual(segment.Memory.Span));
    }

    [Fact]
    public void StreamBackedBuffer_RejectsNonSeekableStream()
    {
        using var inner = new MemoryStream(MakeData(16));
        using var nonSeekable = new NonSeekableStream(inner);

        Assert.Throws<ArgumentException>(() => new StreamBackedBuffer(nonSeekable));
    }

    [Fact]
    public void MappedFileBuffer_ReadsBackExactBytes()
    {
        var data = MakeData(1 << 16);
        var path = Path.Combine(Path.GetTempPath(), $"lucitex-buffer-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data);

        try
        {
            using var buffer = new MappedFileBuffer(path);

            Assert.Equal(data.Length, buffer.Length);

            var segment = buffer.GetSegment(1000, 2000);

            Assert.True(data.AsSpan(1000, 2000).SequenceEqual(segment.Memory.Span));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
