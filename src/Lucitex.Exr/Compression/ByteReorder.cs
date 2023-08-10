namespace Lucitex.Exr.Compression;

internal static class ByteReorder
{
    public static void Split(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var half = (data.Length + 1) / 2;
        var t1 = 0;
        var t2 = half;
        var s = 0;

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

        while (s < data.Length) {
            destination[s++] = data[t1++];
            if (s < data.Length) {
                destination[s++] = data[t2++];
            }
        }
    }
}
