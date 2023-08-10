using System.IO.MemoryMappedFiles;

namespace Lucitex.Core.Execution.Buffers;

public sealed class MappedFileBuffer : ILargeBuffer, IDisposable
{
    private readonly MemoryMappedFile _file;
    private bool _disposed;

    public MappedFileBuffer(string path)
    {
        Length = new FileInfo(path).Length;
        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
    }

    public long Length { get; }

    public BufferSegment GetSegment(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (offset < 0 || length < 0 || checked(offset + length) > Length) {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0) {
            return new BufferSegment { Memory = Memory<byte>.Empty, Offset = offset };
        }

        using var accessor = _file.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, length);

        return new BufferSegment { Memory = buffer, Offset = offset };
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _file.Dispose();
        _disposed = true;
    }
}
