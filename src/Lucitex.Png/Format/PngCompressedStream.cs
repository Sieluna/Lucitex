namespace Lucitex.Png.Format;

internal sealed class PngCompressedStream(IReadOnlyList<ReadOnlyMemory<byte>> chunks) : Stream
{
    private int _chunk;
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        var written = 0;
        while (_chunk < chunks.Count && written < destination.Length) {
            var source = chunks[_chunk];
            var count = Math.Min(source.Length - _offset, destination.Length - written);
            source.Span.Slice(_offset, count).CopyTo(destination[written..]);
            written += count;
            _offset += count;
            if (_offset == source.Length) {
                _chunk++;
                _offset = 0;
            }
        }
        return written;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
