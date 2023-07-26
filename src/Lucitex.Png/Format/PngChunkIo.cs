using System.Buffers.Binary;
using System.Text;
using Lucitex.Core.Execution;

namespace Lucitex.Png.Format;

internal readonly record struct PngChunk(string Type, byte[] Data);

internal static class PngChunkIo
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void WriteSignature(Stream stream) => stream.Write(Signature);

    public static void ReadSignature(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);

        if (!buffer.SequenceEqual(Signature))
        {
            throw new ImageFormatException("png", "BadSignature", "Stream does not start with the PNG signature.");
        }
    }

    public static PngChunk ReadChunk(Stream stream)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        stream.ReadExactly(lengthBytes);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);

        Span<byte> typeBytes = stackalloc byte[4];
        stream.ReadExactly(typeBytes);
        var type = Encoding.ASCII.GetString(typeBytes);

        var data = new byte[length];
        stream.ReadExactly(data);

        Span<byte> crcBytes = stackalloc byte[4];
        stream.ReadExactly(crcBytes);
        var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(crcBytes);

        var actualCrc = Crc32.Compute(typeBytes, data);
        if (actualCrc != expectedCrc)
        {
            throw new ImageFormatException("png", "BadChunkCrc", $"CRC mismatch for chunk '{type}'.");
        }

        return new PngChunk(type, data);
    }

    public static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
        stream.Write(lengthBytes);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }
}
