using System.Buffers.Binary;

namespace Lucitex.Png.Format;

internal static class Crc32
{
    private static readonly uint[][] s_Tables = BuildTables();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        crc = Update(crc, data);

        return crc ^ 0xFFFFFFFFu;
    }

    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var crc = 0xFFFFFFFFu;

        crc = Update(crc, first);
        crc = Update(crc, second);

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (offset <= data.Length - sizeof(ulong)) {
            crc ^= BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            crc = s_Tables[7][crc & 0xFF] ^
                s_Tables[6][(crc >> 8) & 0xFF] ^
                s_Tables[5][(crc >> 16) & 0xFF] ^
                s_Tables[4][crc >> 24] ^
                s_Tables[3][data[offset + 4]] ^
                s_Tables[2][data[offset + 5]] ^
                s_Tables[1][data[offset + 6]] ^
                s_Tables[0][data[offset + 7]];
            offset += sizeof(ulong);
        }

        for (; offset < data.Length; offset++) {
            crc = s_Tables[0][(crc ^ data[offset]) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[][] BuildTables()
    {
        var tables = Enumerable.Range(0, 8).Select(_ => new uint[256]).ToArray();

        for (var n = 0u; n < 256; n++) {
            var c = n;
            for (var k = 0; k < 8; k++) {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            tables[0][n] = c;
        }

        for (var table = 1; table < tables.Length; table++) {
            for (var n = 0; n < 256; n++) {
                var c = tables[table - 1][n];
                tables[table][n] = tables[0][c & 0xFF] ^ (c >> 8);
            }
        }

        return tables;
    }
}
