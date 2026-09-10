using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lucitex.Png.Format;

internal static class Crc32
{
    private const int k_CarrylessMinimumLength = 64;

    private static readonly uint[][] s_Tables = BuildTables();

    private static readonly Vector128<ulong> s_Fold512 = Vector128.Create(0x0154442BD4UL, 0x01C6E41596UL);
    private static readonly Vector128<ulong> s_Fold128 = Vector128.Create(0x01751997D0UL, 0x00CCAA009EUL);
    private static readonly Vector128<ulong> s_Fold64 = Vector128.Create(0x0163CD6124UL, 0x0000000000UL);
    private static readonly Vector128<ulong> s_Barrett = Vector128.Create(0x01DB710641UL, 0x01F7011641UL);
    private static readonly Vector128<uint> s_LowLaneMask = Vector128.Create(~0u, 0u, ~0u, 0u);

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
        if (Pclmulqdq.IsSupported && data.Length >= k_CarrylessMinimumLength) {
            return CarrylessUpdate(crc, data);
        }

        return TableUpdate(crc, data);
    }

    private static uint CarrylessUpdate(uint crc, ReadOnlySpan<byte> data)
    {
        ref var source = ref MemoryMarshal.GetReference(data);
        var offset = 0;

        var x1 = Vector128.LoadUnsafe(ref source, 0x00).AsUInt64();
        var x2 = Vector128.LoadUnsafe(ref source, 0x10).AsUInt64();
        var x3 = Vector128.LoadUnsafe(ref source, 0x20).AsUInt64();
        var x4 = Vector128.LoadUnsafe(ref source, 0x30).AsUInt64();

        x1 ^= Vector128.CreateScalar(crc).AsUInt64();
        offset += 64;

        while (offset + 64 <= data.Length) {
            var y1 = Pclmulqdq.CarrylessMultiply(x1, s_Fold512, 0x00);
            var y2 = Pclmulqdq.CarrylessMultiply(x2, s_Fold512, 0x00);
            var y3 = Pclmulqdq.CarrylessMultiply(x3, s_Fold512, 0x00);
            var y4 = Pclmulqdq.CarrylessMultiply(x4, s_Fold512, 0x00);

            x1 = Pclmulqdq.CarrylessMultiply(x1, s_Fold512, 0x11);
            x2 = Pclmulqdq.CarrylessMultiply(x2, s_Fold512, 0x11);
            x3 = Pclmulqdq.CarrylessMultiply(x3, s_Fold512, 0x11);
            x4 = Pclmulqdq.CarrylessMultiply(x4, s_Fold512, 0x11);

            x1 ^= y1 ^ Vector128.LoadUnsafe(ref source, (nuint)offset + 0x00).AsUInt64();
            x2 ^= y2 ^ Vector128.LoadUnsafe(ref source, (nuint)offset + 0x10).AsUInt64();
            x3 ^= y3 ^ Vector128.LoadUnsafe(ref source, (nuint)offset + 0x20).AsUInt64();
            x4 ^= y4 ^ Vector128.LoadUnsafe(ref source, (nuint)offset + 0x30).AsUInt64();

            offset += 64;
        }

        x1 = Fold(x1, x2);
        x1 = Fold(x1, x3);
        x1 = Fold(x1, x4);

        while (offset + 16 <= data.Length) {
            x1 = Fold(x1, Vector128.LoadUnsafe(ref source, (nuint)offset).AsUInt64());
            offset += 16;
        }

        x1 = Sse2.ShiftRightLogical128BitLane(x1.AsByte(), 8).AsUInt64()
            ^ Pclmulqdq.CarrylessMultiply(x1, s_Fold128, 0x10);

        var high = Sse2.ShiftRightLogical128BitLane(x1.AsByte(), 4).AsUInt64();
        x1 = Pclmulqdq.CarrylessMultiply((x1.AsUInt32() & s_LowLaneMask).AsUInt64(), s_Fold64, 0x00) ^ high;

        var reduced = Pclmulqdq.CarrylessMultiply((x1.AsUInt32() & s_LowLaneMask).AsUInt64(), s_Barrett, 0x10);
        reduced = Pclmulqdq.CarrylessMultiply((reduced.AsUInt32() & s_LowLaneMask).AsUInt64(), s_Barrett, 0x00);

        return TableUpdate((x1 ^ reduced).AsUInt32().GetElement(1), data[offset..]);
    }

    private static Vector128<ulong> Fold(Vector128<ulong> value, Vector128<ulong> next) =>
        Pclmulqdq.CarrylessMultiply(value, s_Fold128, 0x11)
            ^ Pclmulqdq.CarrylessMultiply(value, s_Fold128, 0x00)
            ^ next;

    private static uint TableUpdate(uint crc, ReadOnlySpan<byte> data)
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
