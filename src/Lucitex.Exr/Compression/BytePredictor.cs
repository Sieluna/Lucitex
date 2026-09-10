using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lucitex.Exr.Compression;

internal static class BytePredictor
{
    private static readonly Vector128<byte> s_ShiftOne = ShiftControl(1);
    private static readonly Vector128<byte> s_ShiftTwo = ShiftControl(2);
    private static readonly Vector128<byte> s_ShiftFour = ShiftControl(4);
    private static readonly Vector128<byte> s_ShiftEight = ShiftControl(8);
    private static readonly Vector128<byte> s_Broadcast = Vector128.Create((byte)15);

    public static void Apply(Span<byte> data)
    {
        if (data.Length == 0) {
            return;
        }

        var end = data.Length;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            var bias = new Vector<byte>(128);
            while (end - lanes >= 1) {
                var i = end - lanes;
                var current = new Vector<byte>(data.Slice(i, lanes));
                var previous = new Vector<byte>(data.Slice(i - 1, lanes));
                (current - previous + bias).CopyTo(data.Slice(i, lanes));
                end = i;
            }
        }

        for (var i = end - 1; i >= 1; i--) {
            data[i] = unchecked((byte)(data[i] - data[i - 1] + 128));
        }
    }

    public static void Remove(Span<byte> data)
    {
        if (data.Length == 0) {
            return;
        }

        var i = 1;

        if (Vector128.IsHardwareAccelerated && data.Length > 16) {
            ref var start = ref MemoryMarshal.GetReference(data);
            var bias = Vector128.Create((byte)128);
            var carry = Vector128.Create(data[0]);

            for (; i + 16 <= data.Length; i += 16) {
                var block = Vector128.LoadUnsafe(ref start, (nuint)i) - bias;
                block += Vector128.Shuffle(block, s_ShiftOne);
                block += Vector128.Shuffle(block, s_ShiftTwo);
                block += Vector128.Shuffle(block, s_ShiftFour);
                block += Vector128.Shuffle(block, s_ShiftEight);
                block += carry;

                block.StoreUnsafe(ref start, (nuint)i);
                carry = Vector128.Shuffle(block, s_Broadcast);
            }
        }

        var previous = data[i - 1];
        for (; i < data.Length; i++) {
            var current = unchecked((byte)(previous + data[i] - 128));
            data[i] = current;
            previous = current;
        }
    }

    private static Vector128<byte> ShiftControl(int distance)
    {
        Span<byte> control = stackalloc byte[16];
        for (var i = 0; i < 16; i++) {
            control[i] = i >= distance ? (byte)(i - distance) : (byte)0xFF;
        }

        return Vector128.Create<byte>(control);
    }
}
