namespace Lucitex.Core.Execution.Buffers;

public readonly struct BufferSegment
{
    public required Memory<byte> Memory { get; init; }

    public required long Offset { get; init; }

    public int Length => Memory.Length;
}

public interface ILargeBuffer
{
    public long Length { get; }

    public BufferSegment GetSegment(long offset, int length);
}
