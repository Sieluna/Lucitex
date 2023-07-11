namespace Lucitex.Core.Execution.Buffers;

public readonly struct BufferSegment
{
    public required Memory<byte> Memory { get; init; }

    public required long Offset { get; init; }

    public int Length => Memory.Length;
}

public interface ILargeBuffer
{
    long Length { get; }

    BufferSegment GetSegment(long offset, int length);
}
