using System.Buffers.Binary;

namespace Lucitex.Ktx2.Format;

internal sealed class Ktx2BinaryReader(Stream stream, int maxAllocationLength = int.MaxValue)
{
    public Stream Stream { get; } = stream;

    public uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public ulong ReadUInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0 || count > maxAllocationLength) {
            throw new InvalidDataException($"KTX2 field length {count} exceeds the allowed maximum of {maxAllocationLength}.");
        }

        var buffer = new byte[count];
        Stream.ReadExactly(buffer);
        return buffer;
    }
}

internal sealed class Ktx2BinaryWriter(Stream stream)
{
    public Stream Stream { get; } = stream;

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes) => Stream.Write(bytes);
}
