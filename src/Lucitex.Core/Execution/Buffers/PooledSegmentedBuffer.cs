using System.Buffers;

namespace Lucitex.Core.Execution.Buffers;

public sealed class PooledSegmentedBuffer : ILargeBuffer, IDisposable
{
    private const int ChunkSize = 1 << 20;

    private readonly List<byte[]> _chunks = [];
    private long _length;
    private bool _disposed;

    public long Length => _length;

    public void Append(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var remaining = data;
        while (!remaining.IsEmpty)
        {
            var chunkIndex = (int)(_length / ChunkSize);
            var chunkOffset = (int)(_length % ChunkSize);

            while (_chunks.Count <= chunkIndex)
            {
                _chunks.Add(ArrayPool<byte>.Shared.Rent(ChunkSize));
            }

            var writable = Math.Min(remaining.Length, ChunkSize - chunkOffset);
            remaining[..writable].CopyTo(_chunks[chunkIndex].AsSpan(chunkOffset, writable));

            _length += writable;
            remaining = remaining[writable..];
        }
    }

    public BufferSegment GetSegment(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (offset < 0 || length < 0 || checked(offset + length) > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var startIndex = (int)(offset / ChunkSize);
        var startOffset = (int)(offset % ChunkSize);

        if (startOffset + length <= ChunkSize)
        {
            return new BufferSegment { Memory = _chunks[startIndex].AsMemory(startOffset, length), Offset = offset };
        }

        var buffer = new byte[length];
        var written = 0;
        var remaining = length;
        var chunkIndex = startIndex;
        var chunkOffset = startOffset;

        while (remaining > 0)
        {
            var available = Math.Min(remaining, ChunkSize - chunkOffset);
            _chunks[chunkIndex].AsSpan(chunkOffset, available).CopyTo(buffer.AsSpan(written, available));

            written += available;
            remaining -= available;
            chunkIndex++;
            chunkOffset = 0;
        }

        return new BufferSegment { Memory = buffer, Offset = offset };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var chunk in _chunks)
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        _chunks.Clear();
        _disposed = true;
    }
}
