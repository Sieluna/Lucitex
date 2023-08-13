using System.Buffers.Binary;
using System.Text;

namespace Lucitex.Ktx2.Format;

public sealed record Ktx2KeyValueEntry(string Key, byte[] Value);

internal static class Ktx2KeyValueIo
{
    public static List<Ktx2KeyValueEntry> Read(byte[] data)
    {
        var entries = new List<Ktx2KeyValueEntry>();
        var offset = 0;

        while (offset < data.Length) {
            if (offset + 4 > data.Length) {
                throw new EndOfStreamException("Truncated KTX2 key/value entry length.");
            }

            var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            offset += 4;

            if (length == 0 || offset + length > data.Length) {
                throw new InvalidDataException("KTX2 key/value entry length is invalid.");
            }

            var keyAndValue = data.AsSpan(offset, (int)length);
            var nullIndex = keyAndValue.IndexOf((byte)0);
            if (nullIndex < 0) {
                throw new InvalidDataException("KTX2 key/value entry is missing its key terminator.");
            }

            var key = Encoding.UTF8.GetString(keyAndValue[..nullIndex]);
            var value = keyAndValue[(nullIndex + 1)..].ToArray();
            entries.Add(new Ktx2KeyValueEntry(key, value));

            offset += (int)length;
            offset += (int)((4 - (length % 4)) % 4);
        }

        return entries;
    }

    public static byte[] Write(IReadOnlyList<Ktx2KeyValueEntry> entries)
    {
        using var buffer = new MemoryStream();
        Span<byte> lengthBytes = stackalloc byte[4];

        foreach (var entry in entries) {
            var keyBytes = Encoding.UTF8.GetBytes(entry.Key);
            var length = keyBytes.Length + 1 + entry.Value.Length;

            BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)length);
            buffer.Write(lengthBytes);
            buffer.Write(keyBytes);
            buffer.WriteByte(0);
            buffer.Write(entry.Value);

            var padding = (4 - (length % 4)) % 4;
            for (var i = 0; i < padding; i++) {
                buffer.WriteByte(0);
            }
        }

        return buffer.ToArray();
    }
}
