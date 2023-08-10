namespace Lucitex.Core.Execution.Buffers;

public sealed class StreamBackedBuffer : ILargeBuffer
{
    private readonly Stream _stream;

    public StreamBackedBuffer(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanRead) {
            throw new ArgumentException("Stream must be seekable and readable.", nameof(stream));
        }

        _stream = stream;
    }

    public long Length => _stream.Length;

    public BufferSegment GetSegment(long offset, int length)
    {
        if (offset < 0 || length < 0 || checked(offset + length) > Length) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var buffer = new byte[length];
        _stream.Position = offset;

        var read = 0;
        while (read < length) {
            var count = _stream.Read(buffer, read, length - read);
            if (count == 0) {
                throw new EndOfStreamException();
            }

            read += count;
        }

        return new BufferSegment { Memory = buffer, Offset = offset };
    }
}
