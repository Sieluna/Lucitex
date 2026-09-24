using System.Buffers;

namespace Lucitex.Png.Format;

internal sealed class PngIdatStream(Stream output) : Stream
{
    private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(65536);
    private int _count;
    private bool _disposed;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty) {
            var count = Math.Min(buffer.Length, _buffer.Length - _count);
            buffer[..count].CopyTo(_buffer.AsSpan(_count));
            _count += count;
            buffer = buffer[count..];
            if (_count == _buffer.Length) {
                Flush();
            }
        }
    }

    public override void Flush()
    {
        if (_count > 0) {
            PngChunkIo.WriteChunk(output, "IDAT", _buffer.AsSpan(0, _count));
            _count = 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed) {
            _disposed = true;
            if (disposing) {
                Flush();
            }
            ArrayPool<byte>.Shared.Return(_buffer);
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
