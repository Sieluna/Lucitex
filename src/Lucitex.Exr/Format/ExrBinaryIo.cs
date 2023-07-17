using System.Buffers.Binary;
using System.Text;

namespace Lucitex.Exr.Format;

internal sealed class ExrBinaryReader(Stream stream)
{
    public Stream Stream { get; } = stream;

    public byte ReadByte()
    {
        var value = Stream.ReadByte();
        if (value < 0)
        {
            throw new EndOfStreamException();
        }

        return (byte)value;
    }

    public byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        Stream.ReadExactly(buffer);
        return buffer;
    }

    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public float ReadFloat()
    {
        Span<byte> buffer = stackalloc byte[4];
        Stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public string ReadCString()
    {
        var bytes = new List<byte>(32);
        while (true)
        {
            var b = ReadByte();
            if (b == 0)
            {
                break;
            }

            bytes.Add(b);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}

internal sealed class ExrBinaryWriter(Stream stream)
{
    public Stream Stream { get; } = stream;

    public void WriteByte(byte value) => Stream.WriteByte(value);

    public void WriteBytes(ReadOnlySpan<byte> bytes) => Stream.Write(bytes);

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteFloat(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        Stream.Write(buffer);
    }

    public void WriteCString(string value)
    {
        Stream.Write(Encoding.UTF8.GetBytes(value));
        Stream.WriteByte(0);
    }
}
