using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lucitex.Exr.Compression;

internal static class ByteReorder
{
    private static readonly Vector128<byte> s_EvenPack = Vector128.Create(
        (byte)0, 2, 4, 6, 8, 10, 12, 14, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);

    private static readonly Vector128<byte> s_OddPack = Vector128.Create(
        (byte)1, 3, 5, 7, 9, 11, 13, 15, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);

    private static readonly Vector128<byte> s_InterleaveEvenLow = Vector128.Create(
        (byte)0, 0xFF, 1, 0xFF, 2, 0xFF, 3, 0xFF, 4, 0xFF, 5, 0xFF, 6, 0xFF, 7, 0xFF);

    private static readonly Vector128<byte> s_InterleaveOddLow = Vector128.Create(
        (byte)0xFF, 0, 0xFF, 1, 0xFF, 2, 0xFF, 3, 0xFF, 4, 0xFF, 5, 0xFF, 6, 0xFF, 7);

    private static readonly Vector128<byte> s_InterleaveEvenHigh = Vector128.Create(
        (byte)8, 0xFF, 9, 0xFF, 10, 0xFF, 11, 0xFF, 12, 0xFF, 13, 0xFF, 14, 0xFF, 15, 0xFF);

    private static readonly Vector128<byte> s_InterleaveOddHigh = Vector128.Create(
        (byte)0xFF, 8, 0xFF, 9, 0xFF, 10, 0xFF, 11, 0xFF, 12, 0xFF, 13, 0xFF, 14, 0xFF, 15);

    public static void Split(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var half = (data.Length + 1) / 2;
        var t1 = 0;
        var t2 = half;
        var s = 0;

        if (Vector128.IsHardwareAccelerated) {
            ref var sourceReference = ref MemoryMarshal.GetReference(data);
            ref var destinationReference = ref MemoryMarshal.GetReference(destination);
            while (s <= data.Length - 32) {
                var first = Vector128.LoadUnsafe(ref sourceReference, (nuint)s);
                var second = Vector128.LoadUnsafe(ref sourceReference, (nuint)(s + 16));
                var evens = Vector128.Create(
                    Vector128.Shuffle(first, s_EvenPack).GetLower(),
                    Vector128.Shuffle(second, s_EvenPack).GetLower());
                var odds = Vector128.Create(
                    Vector128.Shuffle(first, s_OddPack).GetLower(),
                    Vector128.Shuffle(second, s_OddPack).GetLower());
                evens.StoreUnsafe(ref destinationReference, (nuint)t1);
                odds.StoreUnsafe(ref destinationReference, (nuint)t2);
                s += 32;
                t1 += 16;
                t2 += 16;
            }
        }

        while (s < data.Length) {
            destination[t1++] = data[s++];
            if (s < data.Length) {
                destination[t2++] = data[s++];
            }
        }
    }

    public static void Interleave(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var half = (data.Length + 1) / 2;
        var t1 = 0;
        var t2 = half;
        var s = 0;

        if (Vector128.IsHardwareAccelerated) {
            ref var sourceReference = ref MemoryMarshal.GetReference(data);
            ref var destinationReference = ref MemoryMarshal.GetReference(destination);
            while (s <= data.Length - 32) {
                var evens = Vector128.LoadUnsafe(ref sourceReference, (nuint)t1);
                var odds = Vector128.LoadUnsafe(ref sourceReference, (nuint)t2);
                var low = Vector128.Shuffle(evens, s_InterleaveEvenLow) |
                    Vector128.Shuffle(odds, s_InterleaveOddLow);
                var high = Vector128.Shuffle(evens, s_InterleaveEvenHigh) |
                    Vector128.Shuffle(odds, s_InterleaveOddHigh);
                low.StoreUnsafe(ref destinationReference, (nuint)s);
                high.StoreUnsafe(ref destinationReference, (nuint)(s + 16));
                s += 32;
                t1 += 16;
                t2 += 16;
            }
        }

        while (s < data.Length) {
            destination[s++] = data[t1++];
            if (s < data.Length) {
                destination[s++] = data[t2++];
            }
        }
    }
}
