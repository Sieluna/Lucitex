using System.Buffers.Binary;

namespace Lucitex.Dds.Format;

internal sealed class DdsBinaryReader(Stream stream, int maxAllocationLength = int.MaxValue)
{
    public Stream Stream { get; } = stream;

    public uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || count > maxAllocationLength) {
            throw new InvalidDataException($"DDS field length {count} exceeds the allowed maximum of {maxAllocationLength}.");
        }

        var buffer = new byte[count];
        Stream.ReadExactly(buffer);
        return buffer;
    }
}

internal sealed class DdsBinaryWriter(Stream stream)
{
    public Stream Stream { get; } = stream;

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes) => Stream.Write(bytes);
}
