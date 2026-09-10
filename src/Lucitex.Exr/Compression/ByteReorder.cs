using System.Numerics;

namespace Lucitex.Exr.Compression;

internal static class ByteReorder
{
    public static void Split(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var half = (data.Length + 1) / 2;
        var t1 = 0;
        var t2 = half;
        var s = 0;

        if (Vector.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            var lanes = Vector<byte>.Count;
            var low = new Vector<ushort>(0x00FF);

            while (s + (2 * lanes) <= data.Length) {
                var first = Vector.As<byte, ushort>(new Vector<byte>(data.Slice(s, lanes)));
                var second = Vector.As<byte, ushort>(new Vector<byte>(data.Slice(s + lanes, lanes)));

                Vector.Narrow(first & low, second & low).CopyTo(destination.Slice(t1, lanes));
                Vector.Narrow(first >> 8, second >> 8).CopyTo(destination.Slice(t2, lanes));

                s += 2 * lanes;
                t1 += lanes;
                t2 += lanes;
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

        if (Vector.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            var lanes = Vector<byte>.Count;

            while (s + (2 * lanes) <= data.Length) {
                Vector.Widen(new Vector<byte>(data.Slice(t1, lanes)), out var evenLow, out var evenHigh);
                Vector.Widen(new Vector<byte>(data.Slice(t2, lanes)), out var oddLow, out var oddHigh);

                Vector.As<ushort, byte>(evenLow | (oddLow << 8)).CopyTo(destination.Slice(s, lanes));
                Vector.As<ushort, byte>(evenHigh | (oddHigh << 8)).CopyTo(destination.Slice(s + lanes, lanes));

                s += 2 * lanes;
                t1 += lanes;
                t2 += lanes;
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
