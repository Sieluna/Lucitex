namespace Lucitex.Exr.Compression;

internal static class BytePredictor
{
    public static void Apply(Span<byte> data)
    {
        if (data.Length == 0) {
            return;
        }

        var previous = data[0];
        for (var i = 1; i < data.Length; i++) {
            var current = data[i];
            var delta = current - previous + 128 + 256;
            previous = current;
            data[i] = unchecked((byte)delta);
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
