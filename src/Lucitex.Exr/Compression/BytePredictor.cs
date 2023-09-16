using System.Numerics;

namespace Lucitex.Exr.Compression;

internal static class BytePredictor
{
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

        var previous = data[0];
        for (var i = 1; i < data.Length; i++) {
            var value = previous + data[i] - 128;
            var current = unchecked((byte)value);
            data[i] = current;
            previous = current;
        }
    }
}
